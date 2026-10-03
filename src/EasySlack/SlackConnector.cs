namespace EasySlack
{
    using EasySlack.Internal;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Implements a native Slack connector using Slack Web API and Socket Mode.
    /// </summary>
    public class SlackConnector : ISlackConnector
    {
        /// <summary>
        /// Gets or sets the diagnostic logger callback.
        /// </summary>
        public Action<string>? Logger { get; set; }

        /// <summary>
        /// Fired when a plain Slack message event is received.
        /// Events with a non-empty subtype are skipped.
        /// </summary>
        public event AsyncEventHandler<SlackMessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// Fired when the connector establishes or re-establishes a Socket Mode connection.
        /// </summary>
        public event AsyncEventHandler<SlackConnectedEventArgs>? Connected;

        /// <summary>
        /// Fired when the connector loses its Socket Mode connection.
        /// </summary>
        public event AsyncEventHandler<SlackDisconnectedEventArgs>? Disconnected;

        /// <summary>
        /// Fired when Slack reports a condition that likely requires user intervention.
        /// </summary>
        public event AsyncEventHandler<SlackActionRequiredEventArgs>? ActionRequired;

        /// <summary>
        /// Gets the current connection state.
        /// </summary>
        public SlackConnectionState ConnectionState
        {
            get
            {
                lock (_StateLock)
                {
                    return _ConnectionState;
                }
            }
        }

        private readonly object _StateLock = new object();
        private readonly SlackConnectorOptions _Options;
        private readonly HttpClient _HttpClient;
        private readonly bool _OwnsHttpClient;
        private readonly IManagedWebSocketFactory _WebSocketFactory;
        private readonly SocketModeEnvelopeProcessor _EnvelopeProcessor;
        private readonly CancellationTokenSource _LifetimeCancellationTokenSource;
        private readonly bool _OwnsLifetimeCancellationTokenSource;

        private CancellationTokenSource? _RunCancellationTokenSource;
        private IManagedWebSocket? _WebSocket;
        private Task? _ReceiveTask;
        private bool _Disposed = false;
        private SlackConnectionState _ConnectionState = SlackConnectionState.Disconnected;
        private ActivityContext _ConnectionActivityContext = default(ActivityContext);

        /// <summary>
        /// Initializes a new instance of the <see cref="SlackConnector"/> class.
        /// </summary>
        /// <param name="options">The connector options.</param>
        public SlackConnector(SlackConnectorOptions options)
            : this(options, new CancellationTokenSource(), new HttpClient(), true, new ManagedClientWebSocketFactory(), new SocketModeEnvelopeProcessor(), true)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SlackConnector"/> class.
        /// </summary>
        /// <param name="options">The connector options.</param>
        /// <param name="cancellationTokenSource">The lifetime cancellation token source.</param>
        public SlackConnector(SlackConnectorOptions options, CancellationTokenSource cancellationTokenSource)
            : this(options, cancellationTokenSource, new HttpClient(), true, new ManagedClientWebSocketFactory(), new SocketModeEnvelopeProcessor(), false)
        {
        }

        internal SlackConnector(
            SlackConnectorOptions options,
            CancellationTokenSource cancellationTokenSource,
            HttpClient httpClient,
            bool ownsHttpClient,
            IManagedWebSocketFactory webSocketFactory,
            SocketModeEnvelopeProcessor envelopeProcessor)
            : this(options, cancellationTokenSource, httpClient, ownsHttpClient, webSocketFactory, envelopeProcessor, false)
        {
        }

        private SlackConnector(
            SlackConnectorOptions options,
            CancellationTokenSource cancellationTokenSource,
            HttpClient httpClient,
            bool ownsHttpClient,
            IManagedWebSocketFactory webSocketFactory,
            SocketModeEnvelopeProcessor envelopeProcessor,
            bool ownsLifetimeCancellationTokenSource)
        {
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            _LifetimeCancellationTokenSource = cancellationTokenSource ?? throw new ArgumentNullException(nameof(cancellationTokenSource));
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _OwnsHttpClient = ownsHttpClient;
            _WebSocketFactory = webSocketFactory ?? throw new ArgumentNullException(nameof(webSocketFactory));
            _EnvelopeProcessor = envelopeProcessor ?? throw new ArgumentNullException(nameof(envelopeProcessor));
            _OwnsLifetimeCancellationTokenSource = ownsLifetimeCancellationTokenSource;

            if (_HttpClient.BaseAddress == null)
            {
                _HttpClient.BaseAddress = new Uri(_Options.ApiBaseUrl, UriKind.Absolute);
            }
        }

        /// <summary>
        /// Starts the Socket Mode connection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the initial connection is established.</returns>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            Log("Starting connector.");

            long started = Stopwatch.GetTimestamp();
            using Activity? activity = EasySlackTelemetry.StartOperation(EasySlackTelemetryNames.OperationStart);

            try
            {
                lock (_StateLock)
                {
                    if (_ConnectionState != SlackConnectionState.Disconnected || IsReceiveLoopActiveLocked())
                    {
                        throw new InvalidOperationException("The Slack connector is already started.");
                    }

                    // A previous run whose receive loop has already exited (for example, a drop with auto-reconnect disabled)
                    // still holds its token source; release it before starting over.
                    CleanupRunState();
                    SetStateLocked(SlackConnectionState.Connecting);
                    _RunCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_LifetimeCancellationTokenSource.Token);
                }

                bool connected = false;

                try
                {
                    using (CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_RunCancellationTokenSource.Token, cancellationToken))
                    {
                        await ConnectSocketAsync(linkedSource.Token).ConfigureAwait(false);
                    }

                    connected = true;
                }
                finally
                {
                    if (!connected)
                    {
                        lock (_StateLock)
                        {
                            SetStateLocked(SlackConnectionState.Disconnected);
                        }

                        CleanupRunState();
                    }
                }

                EasySlackTelemetry.CompleteOperation(activity, EasySlackTelemetryNames.OperationStart, started, EasySlackTelemetryNames.OutcomeSuccess, null);
            }
            catch (Exception exception) when (EasySlackTelemetry.FailOperation(activity, EasySlackTelemetryNames.OperationStart, started, exception))
            {
                throw;
            }
        }

        /// <summary>
        /// Stops the Socket Mode connection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the connector is stopped.</returns>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_Disposed) return;
            Log("Stopping connector.");

            Task? receiveTask = null;
            IManagedWebSocket? webSocket = null;
            CancellationTokenSource? runTokenSource = null;

            lock (_StateLock)
            {
                // The state is also Disconnected while the receive loop waits out a reconnect backoff, so only treat the
                // connector as stopped when no receive loop is running.
                if (_ConnectionState == SlackConnectionState.Disconnected && !IsReceiveLoopActiveLocked())
                {
                    CleanupRunState();
                    return;
                }

                SetStateLocked(SlackConnectionState.Stopping);
                receiveTask = _ReceiveTask;
                webSocket = _WebSocket;
                runTokenSource = _RunCancellationTokenSource;
            }

            long started = Stopwatch.GetTimestamp();
            using Activity? activity = EasySlackTelemetry.StartOperation(EasySlackTelemetryNames.OperationStop);

            try
            {
                await StopRunningAsync(receiveTask, webSocket, runTokenSource, cancellationToken).ConfigureAwait(false);
                EasySlackTelemetry.CompleteOperation(activity, EasySlackTelemetryNames.OperationStop, started, EasySlackTelemetryNames.OutcomeSuccess, null);
            }
            catch (Exception exception) when (EasySlackTelemetry.FailOperation(activity, EasySlackTelemetryNames.OperationStop, started, exception))
            {
                throw;
            }
        }

        /// <summary>
        /// Validates the configured Slack bot token.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The validation result.</returns>
        public async Task<SlackValidationResult> ValidateConnectionAsync(CancellationToken cancellationToken = default)
        {
            long started = Stopwatch.GetTimestamp();
            using Activity? activity = EasySlackTelemetry.StartOperation(EasySlackTelemetryNames.OperationValidateConnection);

            try
            {
                SlackValidationResult result = await ValidateConnectionCoreAsync(cancellationToken).ConfigureAwait(false);
                EasySlackTelemetry.CompleteOperationResult(activity, EasySlackTelemetryNames.OperationValidateConnection, started, result.Ok, result.Error);
                return result;
            }
            catch (Exception exception) when (EasySlackTelemetry.FailOperation(activity, EasySlackTelemetryNames.OperationValidateConnection, started, exception))
            {
                throw;
            }
        }

        private async Task<SlackValidationResult> ValidateConnectionCoreAsync(CancellationToken cancellationToken)
        {
            Log("Validating bot token with auth.test.");
            JsonDocument document = await SendApiRequestAsync(HttpMethod.Get, "auth.test", _Options.Auth.BotToken, null, cancellationToken).ConfigureAwait(false);
            using (document)
            {
                JsonElement root = document.RootElement;
                SlackValidationResult result = new SlackValidationResult
                {
                    Ok = ReadBoolean(root, "ok"),
                    Error = ReadString(root, "error"),
                    TeamId = ReadString(root, "team_id"),
                    TeamName = ReadString(root, "team"),
                    UserId = ReadString(root, "user_id"),
                    UserName = ReadString(root, "user"),
                    BotId = ReadString(root, "bot_id")
                };

                return result;
            }
        }

        /// <summary>
        /// Sends a message to a Slack user by opening or reusing a direct conversation.
        /// </summary>
        /// <param name="userId">The Slack user identifier.</param>
        /// <param name="text">The message text.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The send result.</returns>
        public async Task<SlackSendMessageResult> SendMessageToUserAsync(string userId, string text, CancellationToken cancellationToken = default)
        {
            long started = Stopwatch.GetTimestamp();
            using Activity? activity = EasySlackTelemetry.StartOperation(EasySlackTelemetryNames.OperationSendMessageToUser);

            try
            {
                SlackSendMessageResult result = await SendMessageToUserCoreAsync(userId, text, cancellationToken).ConfigureAwait(false);
                EasySlackTelemetry.CompleteOperationResult(activity, EasySlackTelemetryNames.OperationSendMessageToUser, started, result.Ok, result.Error);
                return result;
            }
            catch (Exception exception) when (EasySlackTelemetry.FailOperation(activity, EasySlackTelemetryNames.OperationSendMessageToUser, started, exception))
            {
                throw;
            }
        }

        private async Task<SlackSendMessageResult> SendMessageToUserCoreAsync(string userId, string text, CancellationToken cancellationToken)
        {
            string sanitizedUserId = RequireValue(userId, nameof(userId));
            string conversationId = await OpenDirectConversationAsync(sanitizedUserId, cancellationToken).ConfigureAwait(false);
            EasySlackTelemetry.SetTag(Activity.Current, EasySlackTelemetryNames.AttributeChannelId, conversationId);
            return await SendMessageToChannelCoreAsync(conversationId, text, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a message to a Slack channel or conversation.
        /// </summary>
        /// <param name="channelId">The Slack conversation identifier.</param>
        /// <param name="text">The message text.</param>
        /// <param name="threadTimestamp">The Slack thread root timestamp to reply into, or <see langword="null"/> for a top-level message.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The send result.</returns>
        public async Task<SlackSendMessageResult> SendMessageToChannelAsync(string channelId, string text, string? threadTimestamp = null, CancellationToken cancellationToken = default)
        {
            long started = Stopwatch.GetTimestamp();
            using Activity? activity = EasySlackTelemetry.StartOperation(EasySlackTelemetryNames.OperationSendMessageToChannel);
            EasySlackTelemetry.SetTag(activity, EasySlackTelemetryNames.AttributeChannelId, channelId);
            EasySlackTelemetry.SetTag(activity, EasySlackTelemetryNames.AttributeThreaded, !string.IsNullOrWhiteSpace(threadTimestamp));

            try
            {
                SlackSendMessageResult result = await SendMessageToChannelCoreAsync(channelId, text, threadTimestamp, cancellationToken).ConfigureAwait(false);
                EasySlackTelemetry.CompleteOperationResult(activity, EasySlackTelemetryNames.OperationSendMessageToChannel, started, result.Ok, result.Error);
                return result;
            }
            catch (Exception exception) when (EasySlackTelemetry.FailOperation(activity, EasySlackTelemetryNames.OperationSendMessageToChannel, started, exception))
            {
                throw;
            }
        }

        private async Task<SlackSendMessageResult> SendMessageToChannelCoreAsync(string channelId, string text, string? threadTimestamp, CancellationToken cancellationToken)
        {
            string sanitizedChannelId = RequireValue(channelId, nameof(channelId));
            string sanitizedText = RequireValue(text, nameof(text));

            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "channel", sanitizedChannelId },
                { "text", sanitizedText }
            };

            if (!string.IsNullOrWhiteSpace(threadTimestamp))
            {
                body["thread_ts"] = threadTimestamp.Trim();
            }

            JsonDocument document = await SendApiRequestAsync(HttpMethod.Post, "chat.postMessage", _Options.Auth.BotToken, body, cancellationToken).ConfigureAwait(false);
            using (document)
            {
                JsonElement root = document.RootElement;
                SlackSendMessageResult result = new SlackSendMessageResult
                {
                    Ok = ReadBoolean(root, "ok"),
                    Error = ReadString(root, "error"),
                    ChannelId = ReadString(root, "channel"),
                    Timestamp = ReadString(root, "ts")
                };

                return result;
            }
        }

        /// <summary>
        /// Retrieves Slack conversation metadata.
        /// </summary>
        /// <param name="channelId">The Slack conversation identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The conversation info result.</returns>
        public async Task<SlackChannelInfoResult> GetChannelInfoAsync(string channelId, CancellationToken cancellationToken = default)
        {
            long started = Stopwatch.GetTimestamp();
            using Activity? activity = EasySlackTelemetry.StartOperation(EasySlackTelemetryNames.OperationGetChannelInfo);
            EasySlackTelemetry.SetTag(activity, EasySlackTelemetryNames.AttributeChannelId, channelId);

            try
            {
                SlackChannelInfoResult result = await GetChannelInfoCoreAsync(channelId, cancellationToken).ConfigureAwait(false);
                EasySlackTelemetry.CompleteOperationResult(activity, EasySlackTelemetryNames.OperationGetChannelInfo, started, result.Ok, result.Error);
                return result;
            }
            catch (Exception exception) when (EasySlackTelemetry.FailOperation(activity, EasySlackTelemetryNames.OperationGetChannelInfo, started, exception))
            {
                throw;
            }
        }

        private async Task<SlackChannelInfoResult> GetChannelInfoCoreAsync(string channelId, CancellationToken cancellationToken)
        {
            string sanitizedChannelId = RequireValue(channelId, nameof(channelId));
            string path = "conversations.info?channel=" + Uri.EscapeDataString(sanitizedChannelId);

            JsonDocument document = await SendApiRequestAsync(HttpMethod.Get, path, _Options.Auth.BotToken, null, cancellationToken).ConfigureAwait(false);
            using (document)
            {
                JsonElement root = document.RootElement;
                SlackChannelInfoResult result = new SlackChannelInfoResult
                {
                    Ok = ReadBoolean(root, "ok"),
                    Error = ReadString(root, "error")
                };

                if (root.TryGetProperty("channel", out JsonElement channel))
                {
                    result.ChannelId = ReadString(channel, "id");
                    result.Name = ReadString(channel, "name");
                    result.IsChannel = ReadBoolean(channel, "is_channel");
                    result.IsPrivate = ReadBoolean(channel, "is_private");
                }

                return result;
            }
        }

        /// <summary>
        /// Retrieves Slack user metadata.
        /// </summary>
        /// <param name="userId">The Slack user identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The user info result.</returns>
        public async Task<SlackUserInfoResult> GetUserInfoAsync(string userId, CancellationToken cancellationToken = default)
        {
            long started = Stopwatch.GetTimestamp();
            using Activity? activity = EasySlackTelemetry.StartOperation(EasySlackTelemetryNames.OperationGetUserInfo);

            try
            {
                SlackUserInfoResult result = await GetUserInfoCoreAsync(userId, cancellationToken).ConfigureAwait(false);
                EasySlackTelemetry.CompleteOperationResult(activity, EasySlackTelemetryNames.OperationGetUserInfo, started, result.Ok, result.Error);
                return result;
            }
            catch (Exception exception) when (EasySlackTelemetry.FailOperation(activity, EasySlackTelemetryNames.OperationGetUserInfo, started, exception))
            {
                throw;
            }
        }

        private async Task<SlackUserInfoResult> GetUserInfoCoreAsync(string userId, CancellationToken cancellationToken)
        {
            string sanitizedUserId = RequireValue(userId, nameof(userId));
            string path = "users.info?user=" + Uri.EscapeDataString(sanitizedUserId);

            JsonDocument document = await SendApiRequestAsync(HttpMethod.Get, path, _Options.Auth.BotToken, null, cancellationToken).ConfigureAwait(false);
            using (document)
            {
                JsonElement root = document.RootElement;
                SlackUserInfoResult result = new SlackUserInfoResult
                {
                    Ok = ReadBoolean(root, "ok"),
                    Error = ReadString(root, "error")
                };

                if (root.TryGetProperty("user", out JsonElement user))
                {
                    result.UserId = ReadString(user, "id");
                    result.UserName = ReadString(user, "name");
                    result.RealName = ReadString(user, "real_name");
                    result.IsBot = ReadBoolean(user, "is_bot");
                    result.IsDeleted = ReadBoolean(user, "deleted");

                    if (user.TryGetProperty("profile", out JsonElement profile))
                    {
                        result.DisplayName = ReadString(profile, "display_name");
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Releases connector resources.
        /// </summary>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases connector resources asynchronously.
        /// </summary>
        /// <returns>A task that completes when disposal is finished.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;

            try
            {
                _LifetimeCancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            await StopAsync().ConfigureAwait(false);

            if (_OwnsHttpClient)
            {
                _HttpClient.Dispose();
            }

            if (_OwnsLifetimeCancellationTokenSource)
            {
                _LifetimeCancellationTokenSource.Dispose();
            }

            MessageReceived = null;
            Connected = null;
            Disconnected = null;
            ActionRequired = null;
            Logger = null;

            _Disposed = true;
            GC.SuppressFinalize(this);
        }

        internal async Task ProcessSocketMessageAsync(string json, CancellationToken cancellationToken)
        {
            Log("Received Socket Mode payload: " + json);
            await _EnvelopeProcessor.ProcessAsync(
                json,
                _ConnectionActivityContext,
                AcknowledgeEnvelopeAsync,
                HandleMessageReceivedAsync,
                HandleDisconnectedEnvelopeAsync,
                HandleActionRequiredAsync,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task StopRunningAsync(Task? receiveTask, IManagedWebSocket? webSocket, CancellationTokenSource? runTokenSource, CancellationToken cancellationToken)
        {

            if (runTokenSource != null) runTokenSource.Cancel();

            if (webSocket != null)
            {
                using (CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linkedSource.CancelAfter(TimeSpan.FromSeconds(5));

                    try
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopping", linkedSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (WebSocketException)
                    {
                    }
                }
            }

            if (receiveTask != null)
            {
                try
                {
                    await receiveTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            CleanupRunState();

            lock (_StateLock)
            {
                SetStateLocked(SlackConnectionState.Disconnected);
            }
        }

        private async Task ConnectSocketAsync(CancellationToken cancellationToken)
        {
            string socketUri = await OpenSocketAsync(false, cancellationToken).ConfigureAwait(false);
            await RaiseConnectedAsync(false, socketUri).ConfigureAwait(false);
        }

        private async Task<string> OpenSocketAsync(bool isReconnect, CancellationToken cancellationToken)
        {
            Log("Opening Socket Mode connection.");

            long started = Stopwatch.GetTimestamp();

            using (Activity? activity = EasySlackTelemetry.StartSocketConnect(isReconnect))
            {
                try
                {
                    string socketUri = await GetSocketModeUriAsync(cancellationToken).ConfigureAwait(false);
                    IManagedWebSocket socket = _WebSocketFactory.Create();
                    Uri uri = new Uri(socketUri, UriKind.Absolute);

                    try
                    {
                        await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
                        Log("Connected WebSocket: " + socketUri);

                        lock (_StateLock)
                        {
                            // StopAsync may have run while the socket was opening; do not resurrect a stopping connector.
                            cancellationToken.ThrowIfCancellationRequested();
                            _WebSocket = socket;
                            _ConnectionActivityContext = activity?.Context ?? default(ActivityContext);
                            SetStateLocked(SlackConnectionState.Connected);

                            if (!isReconnect)
                            {
                                // The receive loop is started exactly once per run. Reconnects happen inside the loop,
                                // which then reads from the replacement socket, so a socket never has two readers.
                                CancellationToken runToken = _RunCancellationTokenSource!.Token;
                                _ReceiveTask = Task.Run(() => ReceiveLoopAsync(runToken), runToken);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        socket.Dispose();
                        throw;
                    }

                    EasySlackTelemetry.CompleteSocketConnect(activity, isReconnect, started, EasySlackTelemetryNames.OutcomeSuccess, null);
                    return socketUri;
                }
                catch (Exception exception) when (EasySlackTelemetry.FailSocketConnect(activity, isReconnect, started, exception))
                {
                    throw;
                }
            }
        }

        private async Task ReconnectAsync(Activity? disconnectActivity, int initialDelayMs, CancellationToken cancellationToken)
        {
            int delayMs = initialDelayMs;
            string socketUri;

            while (true)
            {
                EasySlackTelemetry.RecordReconnectDelay(disconnectActivity, delayMs);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                DisposeSocket();

                lock (_StateLock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SetStateLocked(SlackConnectionState.Connecting);
                }

                try
                {
                    socketUri = await OpenSocketAsync(true, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // A failed attempt (Slack unreachable, apps.connections.open error, handshake failure) must not end
                    // the run or leave the state stuck at Connecting: report Disconnected and retry with backoff.
                    Log("Reconnect attempt failed: " + exception.Message);

                    lock (_StateLock)
                    {
                        if (_ConnectionState == SlackConnectionState.Connecting) SetStateLocked(SlackConnectionState.Disconnected);
                    }

                    delayMs = Math.Min(delayMs * 2, _Options.MaxReconnectDelayMs);
                }
            }

            await RaiseConnectedAsync(true, socketUri).ConfigureAwait(false);
        }

        private async Task RaiseConnectedAsync(bool isReconnect, string socketUri)
        {
            SlackConnectedEventArgs eventArgs = new SlackConnectedEventArgs
            {
                IsReconnect = isReconnect,
                SocketUri = socketUri
            };

            await InvokeEventHandlersAsync(Connected, EasySlackTelemetryNames.EventConnected, eventArgs).ConfigureAwait(false);
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            // The loop is a background unit of work: detach it from the span that started it so each envelope
            // begins its own trace (linked back to the connect span) instead of nesting under it indefinitely.
            Activity.Current = null;

            try
            {
                string exitReason = await RunReceiveLoopAsync(cancellationToken).ConfigureAwait(false);
                EasySlackTelemetry.RecordLoopExit(exitReason, null);
            }
            catch (Exception exception) when (EasySlackTelemetry.FailLoop(exception))
            {
                throw;
            }
        }

        private async Task<string> RunReceiveLoopAsync(CancellationToken cancellationToken)
        {
            int reconnectDelayMs = _Options.InitialReconnectDelayMs;
            Log("Receive loop started.");

            while (!cancellationToken.IsCancellationRequested)
            {
                Exception? dropped = null;

                try
                {
                    IManagedWebSocket? socket = _WebSocket;
                    if (socket == null) return EasySlackTelemetryNames.LoopExitCanceled;

                    string payload = await ReceiveTextMessageAsync(socket, cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(payload)) continue;

                    await ProcessSocketMessageAsync(payload, cancellationToken).ConfigureAwait(false);
                    reconnectDelayMs = _Options.InitialReconnectDelayMs;
                }
                catch (OperationCanceledException)
                {
                    Log("Receive loop canceled.");
                    return EasySlackTelemetryNames.LoopExitCanceled;
                }
                catch (WebSocketException exception)
                {
                    Log("WebSocket exception: " + exception.Message);
                    dropped = exception;
                }
                catch (IOException exception)
                {
                    Log("I/O exception in receive loop: " + exception.Message);
                    dropped = exception;
                }
                catch (JsonException exception)
                {
                    Log("Invalid socket payload: " + exception.Message);
                    SlackActionRequiredEventArgs eventArgs = new SlackActionRequiredEventArgs
                    {
                        Code = "invalid_socket_payload",
                        Description = exception.Message
                    };

                    EasySlackTelemetry.RecordActionRequired(eventArgs.Code);
                    await InvokeEventHandlersAsync(ActionRequired, EasySlackTelemetryNames.EventActionRequired, eventArgs).ConfigureAwait(false);
                }

                if (dropped == null) continue;

                try
                {
                    bool reconnected = await HandleDisconnectAndMaybeReconnectAsync(dropped.Message, dropped, reconnectDelayMs, cancellationToken).ConfigureAwait(false);
                    if (!reconnected) return EasySlackTelemetryNames.LoopExitNoReconnect;
                }
                catch (OperationCanceledException)
                {
                    Log("Receive loop canceled during reconnect.");
                    return EasySlackTelemetryNames.LoopExitCanceled;
                }

                reconnectDelayMs = Math.Min(reconnectDelayMs * 2, _Options.MaxReconnectDelayMs);
            }

            return EasySlackTelemetryNames.LoopExitCanceled;
        }

        private async Task<bool> HandleDisconnectAndMaybeReconnectAsync(string reason, Exception? exception, int reconnectDelayMs, CancellationToken cancellationToken)
        {
            bool willReconnect = _Options.AutoReconnect && !cancellationToken.IsCancellationRequested && !_LifetimeCancellationTokenSource.IsCancellationRequested;
            Log("Disconnected. Will reconnect: " + willReconnect + ". Reason: " + reason);

            SlackDisconnectedEventArgs eventArgs = new SlackDisconnectedEventArgs
            {
                Reason = reason,
                WillReconnect = willReconnect,
                Exception = exception
            };

            EasySlackTelemetry.RecordDisconnect(EasySlackTelemetryNames.DisconnectSourceTransport, willReconnect);
            using Activity? activity = EasySlackTelemetry.StartDisconnect(EasySlackTelemetryNames.DisconnectSourceTransport, reason, willReconnect, _ConnectionActivityContext);
            if (exception != null) EasySlackTelemetry.SetTag(activity, EasySlackTelemetryNames.AttributeErrorType, EasySlackTelemetry.ErrorTypeFor(exception));

            lock (_StateLock)
            {
                if (_ConnectionState != SlackConnectionState.Stopping) SetStateLocked(SlackConnectionState.Disconnected);
            }

            await InvokeEventHandlersAsync(Disconnected, EasySlackTelemetryNames.EventDisconnected, eventArgs).ConfigureAwait(false);

            if (!willReconnect) return false;

            await ReconnectAsync(activity, reconnectDelayMs, cancellationToken).ConfigureAwait(false);
            return true;
        }

        private async Task HandleDisconnectedEnvelopeAsync(SlackDisconnectedEventArgs eventArgs, CancellationToken cancellationToken)
        {
            bool willReconnect = _Options.AutoReconnect && !cancellationToken.IsCancellationRequested && !_LifetimeCancellationTokenSource.IsCancellationRequested;
            eventArgs.WillReconnect = willReconnect;
            Log("Slack disconnect envelope received. Will reconnect: " + willReconnect + ". Reason: " + eventArgs.Reason);

            EasySlackTelemetry.RecordDisconnect(EasySlackTelemetryNames.DisconnectSourceServer, willReconnect);
            using Activity? activity = EasySlackTelemetry.StartDisconnect(EasySlackTelemetryNames.DisconnectSourceServer, eventArgs.Reason, willReconnect, _ConnectionActivityContext);

            lock (_StateLock)
            {
                if (_ConnectionState != SlackConnectionState.Stopping) SetStateLocked(SlackConnectionState.Disconnected);
            }

            await InvokeEventHandlersAsync(Disconnected, EasySlackTelemetryNames.EventDisconnected, eventArgs).ConfigureAwait(false);

            if (!willReconnect) return;

            await ReconnectAsync(activity, _Options.InitialReconnectDelayMs, cancellationToken).ConfigureAwait(false);
        }

        private async Task HandleMessageReceivedAsync(SlackMessageReceivedEventArgs eventArgs, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Subtype))
            {
                Log("Skipping message with subtype " + eventArgs.Subtype + " on channel " + eventArgs.ChannelId + ".");
                EasySlackTelemetry.RecordMessageDisposition(EasySlackTelemetryNames.DispositionSkippedSubtype);
                return;
            }

            Log("Dispatching message event for channel " + eventArgs.ChannelId + ", user " + eventArgs.UserId + ".");
            EasySlackTelemetry.RecordMessageDisposition(EasySlackTelemetryNames.DispositionDispatched);
            EasySlackTelemetry.SetTag(Activity.Current, EasySlackTelemetryNames.AttributeChannelId, eventArgs.ChannelId);
            EasySlackTelemetry.SetTag(Activity.Current, EasySlackTelemetryNames.AttributeThreaded, !string.IsNullOrWhiteSpace(eventArgs.ThreadTimestamp));
            await InvokeEventHandlersAsync(MessageReceived, EasySlackTelemetryNames.EventMessageReceived, eventArgs).ConfigureAwait(false);
        }

        private async Task HandleActionRequiredAsync(SlackActionRequiredEventArgs eventArgs, CancellationToken cancellationToken)
        {
            Log("Action required: " + eventArgs.Code + ". " + eventArgs.Description);
            EasySlackTelemetry.RecordActionRequired(eventArgs.Code);
            await InvokeEventHandlersAsync(ActionRequired, EasySlackTelemetryNames.EventActionRequired, eventArgs).ConfigureAwait(false);
        }

        private async Task AcknowledgeEnvelopeAsync(string envelopeId, CancellationToken cancellationToken)
        {
            IManagedWebSocket? socket = _WebSocket;
            if (socket == null || socket.State != WebSocketState.Open) return;

            string payload = "{\"envelope_id\":\"" + JsonEncodedText.Encode(envelopeId).ToString() + "\"}";
            Log("Acknowledging envelope " + envelopeId + ".");
            await socket.SendTextAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> ReceiveTextMessageAsync(IManagedWebSocket socket, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[_Options.ReceiveBufferSize];
            using (MemoryStream stream = new MemoryStream())
            {
                while (true)
                {
                    ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
                    WebSocketReceiveResult result = await socket.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new WebSocketException("Slack closed the Socket Mode connection.");
                    }

                    if (result.Count > 0)
                    {
                        stream.Write(buffer, 0, result.Count);
                    }

                    if (result.EndOfMessage) break;
                }

                EasySlackTelemetry.RecordMessageSize(stream.Length);

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private async Task<string> GetSocketModeUriAsync(CancellationToken cancellationToken)
        {
            Log("Requesting Socket Mode URL.");
            JsonDocument document = await SendApiRequestAsync(HttpMethod.Post, "apps.connections.open", _Options.Auth.AppToken, null, cancellationToken).ConfigureAwait(false);
            using (document)
            {
                JsonElement root = document.RootElement;
                bool ok = ReadBoolean(root, "ok");
                if (!ok)
                {
                    string error = ReadString(root, "error") ?? "unknown_error";
                    throw new InvalidOperationException("Slack apps.connections.open failed: " + error);
                }

                string socketUri = ReadString(root, "url") ?? throw new InvalidOperationException("Slack did not return a Socket Mode URL.");
                Log("Received Socket Mode URL.");
                return socketUri;
            }
        }

        private async Task<string> OpenDirectConversationAsync(string userId, CancellationToken cancellationToken)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "users", userId }
            };

            JsonDocument document = await SendApiRequestAsync(HttpMethod.Post, "conversations.open", _Options.Auth.BotToken, body, cancellationToken).ConfigureAwait(false);
            using (document)
            {
                JsonElement root = document.RootElement;
                bool ok = ReadBoolean(root, "ok");
                if (!ok)
                {
                    string error = ReadString(root, "error") ?? "unknown_error";
                    throw new InvalidOperationException("Slack conversations.open failed: " + error);
                }

                if (!root.TryGetProperty("channel", out JsonElement channel))
                {
                    throw new InvalidOperationException("Slack conversations.open did not return a channel.");
                }

                string? conversationId = ReadString(channel, "id");
                if (string.IsNullOrWhiteSpace(conversationId))
                {
                    throw new InvalidOperationException("Slack conversations.open returned an empty channel id.");
                }

                return conversationId;
            }
        }

        private async Task<JsonDocument> SendApiRequestAsync(
            HttpMethod method,
            string relativePath,
            string bearerToken,
            object? body,
            CancellationToken cancellationToken)
        {
            Log("HTTP " + method.Method + " " + relativePath);

            string apiMethod = EasySlackTelemetry.ApiMethodFromPath(relativePath);
            long started = Stopwatch.GetTimestamp();
            int statusCode = 0;
            bool recorded = false;
            using Activity? activity = EasySlackTelemetry.StartApi(apiMethod, method.Method, _HttpClient.BaseAddress);

            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(method, relativePath))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

                    if (body != null)
                    {
                        request.Content = JsonContent.Create(body);
                    }

                    using (HttpResponseMessage response = await _HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        statusCode = (int)response.StatusCode;
                        Log("HTTP response " + statusCode + " from " + relativePath);

                        if (!response.IsSuccessStatusCode)
                        {
                            TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
                            if (retryAfter.HasValue) EasySlackTelemetry.SetTag(activity, EasySlackTelemetryNames.AttributeRetryAfter, retryAfter.Value.TotalSeconds);
                            EasySlackTelemetry.CompleteApi(activity, apiMethod, started, statusCode, EasySlackTelemetryNames.OutcomeHttpError, statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            recorded = true;
                        }

                        response.EnsureSuccessStatusCode();
                        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        JsonDocument document = JsonDocument.Parse(json);

                        bool isObject = document.RootElement.ValueKind == JsonValueKind.Object;
                        if (isObject && ReadBoolean(document.RootElement, "ok"))
                        {
                            EasySlackTelemetry.CompleteApi(activity, apiMethod, started, statusCode, EasySlackTelemetryNames.OutcomeSuccess, null);
                        }
                        else
                        {
                            string errorType = EasySlackTelemetry.NormalizeCode(isObject ? ReadString(document.RootElement, "error") : "invalid_response");
                            EasySlackTelemetry.CompleteApi(activity, apiMethod, started, statusCode, EasySlackTelemetryNames.OutcomeSlackError, errorType);
                        }

                        recorded = true;
                        return document;
                    }
                }
            }
            catch (Exception exception) when (!recorded && EasySlackTelemetry.FailApi(activity, apiMethod, started, statusCode, exception))
            {
                throw;
            }
        }

        private async Task InvokeEventHandlersAsync<TEventArgs>(AsyncEventHandler<TEventArgs>? handlers, string eventName, TEventArgs eventArgs) where TEventArgs : EventArgs
        {
            if (handlers == null) return;

            Delegate[] invocationList = handlers.GetInvocationList();
            long started = Stopwatch.GetTimestamp();
            using Activity? activity = EasySlackTelemetry.StartHandler(eventName, invocationList.Length);

            try
            {
                foreach (Delegate entry in invocationList)
                {
                    AsyncEventHandler<TEventArgs> handler = (AsyncEventHandler<TEventArgs>)entry;
                    await handler(this, eventArgs).ConfigureAwait(false);
                }

                EasySlackTelemetry.CompleteHandler(activity, eventName, started, EasySlackTelemetryNames.OutcomeSuccess, null);
            }
            catch (Exception exception) when (EasySlackTelemetry.FailHandler(activity, eventName, started, exception))
            {
                throw;
            }
        }

        private void SetStateLocked(SlackConnectionState next)
        {
            SlackConnectionState previous = _ConnectionState;
            _ConnectionState = next;
            EasySlackTelemetry.RecordStateTransition(previous, next);
        }

        private bool IsReceiveLoopActiveLocked()
        {
            Task? receiveTask = _ReceiveTask;
            return receiveTask != null && !receiveTask.IsCompleted;
        }

        private void DisposeSocket()
        {
            IManagedWebSocket? socket = _WebSocket;
            _WebSocket = null;
            if (socket != null) socket.Dispose();
        }

        private void CleanupRunState()
        {
            DisposeSocket();
            _ReceiveTask = null;

            CancellationTokenSource? runTokenSource = _RunCancellationTokenSource;
            _RunCancellationTokenSource = null;

            if (runTokenSource != null)
            {
                runTokenSource.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
        }

        private void Log(string message)
        {
            Action<string>? logger = Logger;
            if (logger == null) return;
            logger("[EasySlack] " + message);
        }

        private static string RequireValue(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(parameterName);
            return value.Trim();
        }

        private static string? ReadString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement property)) return null;
            if (property.ValueKind == JsonValueKind.Null || property.ValueKind == JsonValueKind.Undefined) return null;
            return property.GetString();
        }

        private static bool ReadBoolean(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement property)) return false;
            if (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False) return false;
            return property.GetBoolean();
        }
    }
}
