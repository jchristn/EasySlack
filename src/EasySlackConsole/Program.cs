namespace EasySlackConsole
{
    using EasySlack;
    using GetSomeInput;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Provides an interactive console for manual EasySlack testing.
    /// </summary>
    public class Program
    {
        private static SlackAuthMaterial? _AuthMaterial = null;
        private static SlackConnector? _SubscriptionConnector = null;
        private static CancellationTokenSource? _SubscriptionCancellationTokenSource = null;
        private static string? _SubscribedChannelId = null;

        /// <summary>
        /// Runs the interactive console.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine();
            WriteHeader();
            await CollectAuthAndValidateAsync().ConfigureAwait(false);
            WriteHelp();

            while (true)
            {
                string command = ReadCommand();
                if (string.IsNullOrWhiteSpace(command)) continue;

                switch (NormalizeCommand(command))
                {
                    case "q":
                        await StopChannelSubscriptionAsync().ConfigureAwait(false);
                        return 0;
                    case "?":
                    case "help":
                        WriteHelp();
                        break;
                    case "validate":
                        await ValidateAsync().ConfigureAwait(false);
                        break;
                    case "user send":
                        await SendMessageToUserAsync().ConfigureAwait(false);
                        break;
                    case "channel send":
                        await SendMessageToChannelAsync().ConfigureAwait(false);
                        break;
                    case "channel sub":
                        await SubscribeToChannelAsync().ConfigureAwait(false);
                        break;
                    case "channel unsub":
                        await StopChannelSubscriptionAsync().ConfigureAwait(false);
                        break;
                    case "status":
                        WriteStatus();
                        break;
                    default:
                        WriteWarning("Unknown command. Enter ? or help to see available commands.");
                        break;
                }
            }
        }

        private static void WriteHeader()
        {
            Console.WriteLine("EasySlack Console");
            Console.WriteLine("-----------------");
            Console.WriteLine();
        }

        private static void WriteHelp()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("  q              Quit");
            Console.WriteLine("  ?/help         Show menu");
            Console.WriteLine("  validate       Validate connectivity");
            Console.WriteLine("  user send      Send to a user");
            Console.WriteLine("  channel send   Send to a channel");
            Console.WriteLine("                 Optionally send as a threaded reply by supplying thread_ts");
            Console.WriteLine("  channel sub    Subscribe to a channel in the background");
            Console.WriteLine("  channel unsub  Stop the active channel subscription");
            Console.WriteLine("  status         Show current subscription status");
            Console.WriteLine();
        }

        private static async Task CollectAuthAndValidateAsync()
        {
            while (true)
            {
                ConfigureAuth();

                if (_AuthMaterial == null)
                {
                    WriteError("Slack auth was not captured.");
                    continue;
                }

                bool validated = await ValidateAsync().ConfigureAwait(false);
                if (validated) return;

                bool retry = Inputty.GetBoolean("Validation failed. Re-enter auth values?", true);
                if (!retry)
                {
                    throw new InvalidOperationException("Unable to continue without valid Slack auth.");
                }

                Console.WriteLine();
            }
        }

        private static void ConfigureAuth()
        {
            string defaultBotToken = _AuthMaterial != null ? _AuthMaterial.BotToken : "xoxb-";
            string defaultAppToken = _AuthMaterial != null ? _AuthMaterial.AppToken : "xapp-";

            string botToken = Inputty.GetString("Slack bot token:", defaultBotToken, false);
            string appToken = Inputty.GetString("Slack app token:", defaultAppToken, false);

            _AuthMaterial = new SlackAuthMaterial(botToken, appToken);
            Console.WriteLine();
        }

        private static async Task<bool> ValidateAsync()
        {
            using (CancellationTokenSource cancellationTokenSource = new CancellationTokenSource())
            {
                using (SlackConnector connector = CreateConnector(cancellationTokenSource))
                {
                    try
                    {
                        SlackValidationResult result = await connector.ValidateConnectionAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                        if (!result.Ok)
                        {
                            WriteError("Slack auth.test failed: " + result.Error);
                            return false;
                        }

                        WriteSuccess("Bot token validated.");
                        Console.WriteLine("Team: " + result.TeamName + " (" + result.TeamId + ")");
                        Console.WriteLine("User: " + result.UserName + " (" + result.UserId + ")");
                        Console.WriteLine("Bot Id: " + result.BotId);

                        Console.WriteLine();
                        Console.WriteLine("Validating Socket Mode startup...");
                        await connector.StartAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                        await connector.StopAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                        WriteSuccess("Socket Mode startup validated.");
                        Console.WriteLine();
                        return true;
                    }
                    catch (Exception exception)
                    {
                        WriteError("Validation failed: " + exception.Message);
                        Console.WriteLine();
                        return false;
                    }
                }
            }
        }

        private static async Task SendMessageToChannelAsync()
        {
            string channelId = Inputty.GetString("Channel or conversation ID:", null, false);
            string text = Inputty.GetString("Message text:", null, false);
            string threadTimestamp = Inputty.GetString("Thread root timestamp (optional):", string.Empty, true);

            using (CancellationTokenSource cancellationTokenSource = new CancellationTokenSource())
            {
                using (SlackConnector connector = CreateConnector(cancellationTokenSource))
                {
                    try
                    {
                        SlackSendMessageResult result = await connector.SendMessageToChannelAsync(channelId, text, threadTimestamp, cancellationTokenSource.Token).ConfigureAwait(false);
                        if (result.Ok)
                        {
                            WriteSuccess("Message sent.");
                            Console.WriteLine("Channel: " + result.ChannelId);
                            Console.WriteLine("Timestamp: " + result.Timestamp);
                            if (!string.IsNullOrWhiteSpace(threadTimestamp))
                            {
                                Console.WriteLine("Thread Root: " + threadTimestamp.Trim());
                            }
                        }
                        else
                        {
                            WriteError("Slack rejected the message: " + result.Error);
                        }
                    }
                    catch (Exception exception)
                    {
                        WriteError("Send failed: " + exception.Message);
                    }
                }
            }

            Console.WriteLine();
        }

        private static async Task SendMessageToUserAsync()
        {
            string userId = Inputty.GetString("Slack user ID:", null, false);
            string text = Inputty.GetString("Message text:", null, false);

            using (CancellationTokenSource cancellationTokenSource = new CancellationTokenSource())
            {
                using (SlackConnector connector = CreateConnector(cancellationTokenSource))
                {
                    try
                    {
                        SlackSendMessageResult result = await connector.SendMessageToUserAsync(userId, text, cancellationTokenSource.Token).ConfigureAwait(false);
                        if (result.Ok)
                        {
                            WriteSuccess("Direct message sent.");
                            Console.WriteLine("Conversation: " + result.ChannelId);
                            Console.WriteLine("Timestamp: " + result.Timestamp);
                        }
                        else
                        {
                            WriteError("Slack rejected the message: " + result.Error);
                        }
                    }
                    catch (Exception exception)
                    {
                        WriteError("Send failed: " + exception.Message);
                    }
                }
            }

            Console.WriteLine();
        }

        private static async Task SubscribeToChannelAsync()
        {
            string channelId = Inputty.GetString("Channel ID to subscribe to:", null, false);
            await StopChannelSubscriptionAsync(false).ConfigureAwait(false);

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            SlackConnector connector = CreateConnector(cancellationTokenSource);
            connector.Connected += OnConnectedAsync;
            connector.Disconnected += OnDisconnectedAsync;
            connector.ActionRequired += OnActionRequiredAsync;
            connector.MessageReceived += async (sender, eventArgs) =>
            {
                if (string.Equals(eventArgs.ChannelId, channelId, StringComparison.Ordinal))
                {
                    WriteInboundMessage(eventArgs);
                }

                await Task.CompletedTask.ConfigureAwait(false);
            };

            try
            {
                await connector.StartAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                _SubscriptionCancellationTokenSource = cancellationTokenSource;
                _SubscriptionConnector = connector;
                _SubscribedChannelId = channelId;
                WriteSuccess("Background subscription started for channel " + channelId + ".");
            }
            catch (Exception exception)
            {
                connector.Dispose();
                cancellationTokenSource.Dispose();
                WriteError("Subscription failed: " + exception.Message);
            }

            Console.WriteLine();
        }

        private static async Task StopChannelSubscriptionAsync(bool writeWhenNotRunning = true)
        {
            SlackConnector? connector = _SubscriptionConnector;
            CancellationTokenSource? cancellationTokenSource = _SubscriptionCancellationTokenSource;
            string? channelId = _SubscribedChannelId;

            if (connector == null || cancellationTokenSource == null)
            {
                if (writeWhenNotRunning)
                {
                    WriteWarning("No active channel subscription.");
                    Console.WriteLine();
                }

                return;
            }

            _SubscriptionConnector = null;
            _SubscriptionCancellationTokenSource = null;
            _SubscribedChannelId = null;

            try
            {
                await connector.StopAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                connector.Dispose();
                cancellationTokenSource.Dispose();
                WriteSuccess("Stopped background subscription" + (!string.IsNullOrWhiteSpace(channelId) ? " for channel " + channelId + "." : "."));
            }
            catch (Exception exception)
            {
                WriteError("Unable to stop subscription cleanly: " + exception.Message);
            }

            Console.WriteLine();
        }

        private static void WriteStatus()
        {
            if (_SubscriptionConnector == null || _SubscriptionCancellationTokenSource == null)
            {
                Console.WriteLine("Subscription: not running");
            }
            else
            {
                Console.WriteLine("Subscription: running");
                Console.WriteLine("Channel: " + _SubscribedChannelId);
                Console.WriteLine("State: " + _SubscriptionConnector.ConnectionState);
            }

            Console.WriteLine();
        }

        private static Task OnConnectedAsync(object sender, SlackConnectedEventArgs eventArgs)
        {
            WriteSuccess("Connected to Socket Mode" + (eventArgs.IsReconnect ? " (reconnected)." : "."));
            Console.WriteLine("Socket: " + eventArgs.SocketUri);
            Console.WriteLine();
            return Task.CompletedTask;
        }

        private static Task OnDisconnectedAsync(object sender, SlackDisconnectedEventArgs eventArgs)
        {
            WriteWarning("Disconnected: " + eventArgs.Reason);
            Console.WriteLine("Will Reconnect: " + eventArgs.WillReconnect);
            if (eventArgs.Exception != null)
            {
                Console.WriteLine("Exception: " + eventArgs.Exception.Message);
            }

            Console.WriteLine();
            return Task.CompletedTask;
        }

        private static Task OnActionRequiredAsync(object sender, SlackActionRequiredEventArgs eventArgs)
        {
            WriteWarning("Action Required: " + eventArgs.Code);
            Console.WriteLine(eventArgs.Description);
            Console.WriteLine();
            return Task.CompletedTask;
        }

        private static SlackConnector CreateConnector(CancellationTokenSource cancellationTokenSource)
        {
            SlackConnectorOptions options = new SlackConnectorOptions(_AuthMaterial!)
            {
                AutoReconnect = true
            };

            SlackConnector connector = new SlackConnector(options, cancellationTokenSource);
            connector.Logger = Console.WriteLine;
            return connector;
        }

        private static string ReadCommand()
        {
            Console.Write("EasySlack [?/help]: ");
            string? command = Console.ReadLine();
            return command ?? string.Empty;
        }

        private static string NormalizeCommand(string command)
        {
            return command.Trim().ToLowerInvariant();
        }

        private static void WriteInboundMessage(SlackMessageReceivedEventArgs eventArgs)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[MESSAGE]");
            Console.ResetColor();
            Console.WriteLine("Channel: " + eventArgs.ChannelId);
            Console.WriteLine("User: " + eventArgs.UserId);
            Console.WriteLine("Timestamp: " + eventArgs.Timestamp);
            Console.WriteLine("Thread Timestamp: " + (eventArgs.ThreadTimestamp ?? "(top-level message)"));
            Console.WriteLine("Text: " + eventArgs.Text);
            Console.WriteLine();
        }

        private static void WriteSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static void WriteWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static void WriteError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}
