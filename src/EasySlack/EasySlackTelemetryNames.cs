namespace EasySlack
{
    /// <summary>
    /// Stable public names for every telemetry point EasySlack emits: the meter and activity source names, every
    /// metric instrument name, every span name, every attribute key, and the bounded attribute values used on metrics.
    /// These names are a public contract consumed by dashboards and alerts. They do not change within a major version.
    /// EasySlack emits through the base class library only (<see cref="System.Diagnostics.Metrics.Meter"/> and
    /// <see cref="System.Diagnostics.ActivitySource"/>). A host collects them by subscribing to
    /// <see cref="MeterName"/> and <see cref="ActivitySourceName"/>. Emission with no subscriber is effectively free.
    /// This class is thread safe (constants only).
    /// </summary>
    public static class EasySlackTelemetryNames
    {
        #region Sources

        /// <summary>
        /// The name of the <see cref="System.Diagnostics.Metrics.Meter"/> EasySlack records metrics on.
        /// </summary>
        public const string MeterName = "EasySlack";

        /// <summary>
        /// The name of the <see cref="System.Diagnostics.ActivitySource"/> EasySlack opens spans on.
        /// </summary>
        public const string ActivitySourceName = "EasySlack";

        #endregion

        #region Metrics

        /// <summary>
        /// Histogram (s). Duration of a public connector operation. Labels: operation, outcome, error.type.
        /// </summary>
        public const string OperationDuration = "easyslack.operation.duration";

        /// <summary>
        /// Counter ({operation}). Public connector operations by outcome. Labels: operation, outcome, error.type.
        /// </summary>
        public const string Operations = "easyslack.operations";

        /// <summary>
        /// Histogram (s). Duration of one Slack Web API call. Labels: slack.api.method, outcome, http.response.status_code, error.type.
        /// </summary>
        public const string ApiRequestDuration = "easyslack.api.request.duration";

        /// <summary>
        /// Counter ({request}). Slack Web API calls by outcome. Labels: slack.api.method, outcome, http.response.status_code, error.type.
        /// </summary>
        public const string ApiRequests = "easyslack.api.requests";

        /// <summary>
        /// Histogram (s). Duration of establishing a Socket Mode connection (apps.connections.open plus the WebSocket handshake). Labels: reconnect, outcome, error.type.
        /// </summary>
        public const string SocketConnectDuration = "easyslack.socket.connect.duration";

        /// <summary>
        /// Counter ({attempt}). Socket Mode connection attempts by outcome. Labels: reconnect, outcome, error.type.
        /// </summary>
        public const string SocketConnects = "easyslack.socket.connects";

        /// <summary>
        /// UpDownCounter ({connection}). Socket Mode connections currently in the Connected state across all connectors in the process.
        /// </summary>
        public const string SocketConnectionsActive = "easyslack.socket.connections.active";

        /// <summary>
        /// Counter ({disconnect}). Socket Mode disconnects. Labels: disconnect.source, will_reconnect.
        /// </summary>
        public const string SocketDisconnects = "easyslack.socket.disconnects";

        /// <summary>
        /// Histogram (s). Backoff delay applied before a reconnect attempt. No labels.
        /// </summary>
        public const string SocketReconnectDelay = "easyslack.socket.reconnect.delay";

        /// <summary>
        /// Counter ({exit}). Receive loop terminations. Labels: loop.exit_reason, error.type.
        /// </summary>
        public const string ReceiveLoopExits = "easyslack.socket.receive_loop.exits";

        /// <summary>
        /// Histogram (By). Size of each inbound Socket Mode text message. No labels.
        /// </summary>
        public const string SocketMessageSize = "easyslack.socket.message.size";

        /// <summary>
        /// Counter ({envelope}). Socket Mode envelopes processed (the pipeline job counter). Labels: envelope.type, outcome, error.type.
        /// </summary>
        public const string Envelopes = "easyslack.envelopes";

        /// <summary>
        /// Histogram (s). End-to-end processing duration of one Socket Mode envelope. Labels: envelope.type, outcome, error.type.
        /// </summary>
        public const string EnvelopeDuration = "easyslack.envelope.duration";

        /// <summary>
        /// Histogram (s). Duration of one envelope pipeline stage (parse, ack, dispatch). Labels: stage, outcome, error.type.
        /// </summary>
        public const string EnvelopeStageDuration = "easyslack.envelope.stage.duration";

        /// <summary>
        /// Counter ({event}). Envelope pipeline stage executions. Labels: stage, outcome, error.type.
        /// </summary>
        public const string EnvelopeStageEvents = "easyslack.envelope.stage.events";

        /// <summary>
        /// Observable gauge (s). Unix time of the most recent successfully processed envelope, across all connectors. Not reported until one succeeds.
        /// </summary>
        public const string EnvelopeLastSuccessTime = "easyslack.envelope.last_success.time";

        /// <summary>
        /// Counter ({event}). Events API events received inside events_api envelopes. Labels: slack.event.type.
        /// </summary>
        public const string EventsReceived = "easyslack.events.received";

        /// <summary>
        /// Counter ({message}). Inbound message events by disposition. Labels: message.disposition.
        /// </summary>
        public const string Messages = "easyslack.messages";

        /// <summary>
        /// Histogram (s). Time spent in application event handlers for one event raise. Labels: event, outcome, error.type.
        /// </summary>
        public const string HandlerDuration = "easyslack.handler.duration";

        /// <summary>
        /// Counter ({invocation}). Application event handler raises by outcome. Labels: event, outcome, error.type.
        /// </summary>
        public const string HandlerInvocations = "easyslack.handler.invocations";

        /// <summary>
        /// Counter ({event}). ActionRequired conditions raised. Labels: action.code.
        /// </summary>
        public const string ActionRequired = "easyslack.action_required";

        /// <summary>
        /// Counter ({error}). Failures by component and error type. Labels: component, error.type.
        /// </summary>
        public const string Errors = "easyslack.errors";

        /// <summary>
        /// Counter ({transition}). Connection state transitions. Labels: connection.state.
        /// </summary>
        public const string StateTransitions = "easyslack.connector.state.transitions";

        /// <summary>
        /// Observable gauge ({info}). Always 1, labeled with the EasySlack library version.
        /// </summary>
        public const string BuildInfo = "easyslack.build.info";

        #endregion

        #region Spans

        /// <summary>
        /// Prefix of a public operation span name. The full name is "easyslack &lt;operation&gt;".
        /// </summary>
        public const string SpanOperationPrefix = "easyslack ";

        /// <summary>
        /// Prefix of a Slack Web API client span name. The full name is "slack &lt;api method&gt;", for example "slack chat.postMessage".
        /// </summary>
        public const string SpanApiPrefix = "slack ";

        /// <summary>
        /// Client span covering apps.connections.open plus the WebSocket handshake.
        /// </summary>
        public const string SpanSocketConnect = "slack socket_mode.connect";

        /// <summary>
        /// Consumer root span for one inbound Socket Mode envelope.
        /// </summary>
        public const string SpanEnvelope = "slack socket_mode.envelope";

        /// <summary>
        /// Prefix of an envelope pipeline stage span name. The full name is "stage:&lt;stage&gt;".
        /// </summary>
        public const string SpanStagePrefix = "stage:";

        /// <summary>
        /// Prefix of an application event handler span name. The full name is "handler:&lt;event&gt;".
        /// </summary>
        public const string SpanHandlerPrefix = "handler:";

        /// <summary>
        /// Root span covering a disconnect, the reconnect backoff, and the reconnect attempt.
        /// </summary>
        public const string SpanDisconnect = "easyslack disconnect";

        #endregion

        #region Attribute-Keys

        /// <summary>
        /// Attribute key: the public operation name.
        /// </summary>
        public const string AttributeOperation = "easyslack.operation";

        /// <summary>
        /// Attribute key: the bounded outcome of an operation, call, stage, or handler.
        /// </summary>
        public const string AttributeOutcome = "easyslack.outcome";

        /// <summary>
        /// Attribute key: the OpenTelemetry error type (exception type name or Slack error code).
        /// </summary>
        public const string AttributeErrorType = "error.type";

        /// <summary>
        /// Attribute key: the Slack Web API method, for example "chat.postMessage".
        /// </summary>
        public const string AttributeApiMethod = "slack.api.method";

        /// <summary>
        /// Attribute key: the HTTP request method.
        /// </summary>
        public const string AttributeHttpRequestMethod = "http.request.method";

        /// <summary>
        /// Attribute key: the HTTP response status code.
        /// </summary>
        public const string AttributeHttpStatusCode = "http.response.status_code";

        /// <summary>
        /// Attribute key: the server host name (span only).
        /// </summary>
        public const string AttributeServerAddress = "server.address";

        /// <summary>
        /// Attribute key: the Slack Retry-After value in seconds on a rate-limited response (span only).
        /// </summary>
        public const string AttributeRetryAfter = "slack.retry_after";

        /// <summary>
        /// Attribute key: whether a connection attempt is a reconnect.
        /// </summary>
        public const string AttributeReconnect = "easyslack.reconnect";

        /// <summary>
        /// Attribute key: the normalized Socket Mode envelope type.
        /// </summary>
        public const string AttributeEnvelopeType = "easyslack.envelope.type";

        /// <summary>
        /// Attribute key: the Socket Mode envelope id (span only).
        /// </summary>
        public const string AttributeEnvelopeId = "slack.envelope.id";

        /// <summary>
        /// Attribute key: the envelope pipeline stage.
        /// </summary>
        public const string AttributeStage = "easyslack.stage";

        /// <summary>
        /// Attribute key: the normalized Events API event type.
        /// </summary>
        public const string AttributeEventType = "slack.event.type";

        /// <summary>
        /// Attribute key: the Slack channel or conversation id (span only).
        /// </summary>
        public const string AttributeChannelId = "slack.channel.id";

        /// <summary>
        /// Attribute key: whether a message is a threaded message or reply (span only).
        /// </summary>
        public const string AttributeThreaded = "slack.message.threaded";

        /// <summary>
        /// Attribute key: the disposition of an inbound message event.
        /// </summary>
        public const string AttributeMessageDisposition = "easyslack.message.disposition";

        /// <summary>
        /// Attribute key: the connector event being raised to application handlers.
        /// </summary>
        public const string AttributeEvent = "easyslack.event";

        /// <summary>
        /// Attribute key: the number of application handlers subscribed to an event (span only).
        /// </summary>
        public const string AttributeHandlerCount = "easyslack.handler.count";

        /// <summary>
        /// Attribute key: the ActionRequired code.
        /// </summary>
        public const string AttributeActionCode = "easyslack.action.code";

        /// <summary>
        /// Attribute key: the source of a disconnect.
        /// </summary>
        public const string AttributeDisconnectSource = "easyslack.disconnect.source";

        /// <summary>
        /// Attribute key: the free-form disconnect reason (span only).
        /// </summary>
        public const string AttributeDisconnectReason = "easyslack.disconnect.reason";

        /// <summary>
        /// Attribute key: whether the connector will reconnect after a disconnect.
        /// </summary>
        public const string AttributeWillReconnect = "easyslack.will_reconnect";

        /// <summary>
        /// Attribute key: the reconnect backoff delay in seconds (span only).
        /// </summary>
        public const string AttributeReconnectDelay = "easyslack.reconnect.delay";

        /// <summary>
        /// Attribute key: the component that failed.
        /// </summary>
        public const string AttributeComponent = "easyslack.component";

        /// <summary>
        /// Attribute key: why the receive loop exited.
        /// </summary>
        public const string AttributeLoopExitReason = "easyslack.loop.exit_reason";

        /// <summary>
        /// Attribute key: the connection state entered.
        /// </summary>
        public const string AttributeConnectionState = "easyslack.connection.state";

        /// <summary>
        /// Attribute key: the EasySlack library version.
        /// </summary>
        public const string AttributeVersion = "easyslack.version";

        /// <summary>
        /// Attribute key: the length of an inbound Socket Mode envelope payload in characters (span only).
        /// </summary>
        public const string AttributeMessageSize = "easyslack.message.size";

        #endregion

        #region Operations

        /// <summary>
        /// Operation: <see cref="ISlackConnector.StartAsync"/>.
        /// </summary>
        public const string OperationStart = "start";

        /// <summary>
        /// Operation: <see cref="ISlackConnector.StopAsync"/>.
        /// </summary>
        public const string OperationStop = "stop";

        /// <summary>
        /// Operation: <see cref="ISlackConnector.ValidateConnectionAsync"/>.
        /// </summary>
        public const string OperationValidateConnection = "validate_connection";

        /// <summary>
        /// Operation: <see cref="ISlackConnector.SendMessageToUserAsync"/>.
        /// </summary>
        public const string OperationSendMessageToUser = "send_message_to_user";

        /// <summary>
        /// Operation: <see cref="ISlackConnector.SendMessageToChannelAsync"/>.
        /// </summary>
        public const string OperationSendMessageToChannel = "send_message_to_channel";

        /// <summary>
        /// Operation: <see cref="ISlackConnector.GetChannelInfoAsync"/>.
        /// </summary>
        public const string OperationGetChannelInfo = "get_channel_info";

        /// <summary>
        /// Operation: <see cref="ISlackConnector.GetUserInfoAsync"/>.
        /// </summary>
        public const string OperationGetUserInfo = "get_user_info";

        #endregion

        #region Outcomes

        /// <summary>
        /// Outcome: completed successfully.
        /// </summary>
        public const string OutcomeSuccess = "success";

        /// <summary>
        /// Outcome: Slack answered HTTP 2xx with "ok": false. error.type carries the Slack error code.
        /// </summary>
        public const string OutcomeSlackError = "slack_error";

        /// <summary>
        /// Outcome: Slack answered with a non-success HTTP status.
        /// </summary>
        public const string OutcomeHttpError = "http_error";

        /// <summary>
        /// Outcome: an exception was thrown.
        /// </summary>
        public const string OutcomeError = "error";

        /// <summary>
        /// Outcome: canceled through a cancellation token.
        /// </summary>
        public const string OutcomeCanceled = "canceled";

        /// <summary>
        /// Outcome: the stage had nothing to do (for example, no envelope id to acknowledge).
        /// </summary>
        public const string OutcomeSkipped = "skipped";

        #endregion

        #region Stages

        /// <summary>
        /// Envelope stage: JSON parse and classification.
        /// </summary>
        public const string StageParse = "parse";

        /// <summary>
        /// Envelope stage: acknowledgement sent back to Slack over the socket.
        /// </summary>
        public const string StageAck = "ack";

        /// <summary>
        /// Envelope stage: routing to the connector and raising application events.
        /// </summary>
        public const string StageDispatch = "dispatch";

        #endregion

        #region Envelope-And-Event-Types

        /// <summary>
        /// Envelope type: events_api.
        /// </summary>
        public const string EnvelopeTypeEventsApi = "events_api";

        /// <summary>
        /// Envelope type: disconnect.
        /// </summary>
        public const string EnvelopeTypeDisconnect = "disconnect";

        /// <summary>
        /// Envelope type: hello.
        /// </summary>
        public const string EnvelopeTypeHello = "hello";

        /// <summary>
        /// Envelope or event type: any other value Slack sent.
        /// </summary>
        public const string TypeOther = "other";

        /// <summary>
        /// Envelope type: the payload could not be parsed.
        /// </summary>
        public const string EnvelopeTypeInvalid = "invalid";

        /// <summary>
        /// Event type: message.
        /// </summary>
        public const string EventTypeMessage = "message";

        /// <summary>
        /// Event type: app_rate_limited.
        /// </summary>
        public const string EventTypeAppRateLimited = "app_rate_limited";

        /// <summary>
        /// Event type: the events_api envelope carried no event.
        /// </summary>
        public const string EventTypeMissing = "missing";

        #endregion

        #region Dispositions-Sources-And-Reasons

        /// <summary>
        /// Message disposition: raised to MessageReceived handlers.
        /// </summary>
        public const string DispositionDispatched = "dispatched";

        /// <summary>
        /// Message disposition: skipped because the message has a subtype.
        /// </summary>
        public const string DispositionSkippedSubtype = "skipped_subtype";

        /// <summary>
        /// Disconnect source: the WebSocket closed or failed.
        /// </summary>
        public const string DisconnectSourceTransport = "transport";

        /// <summary>
        /// Disconnect source: Slack sent a disconnect envelope.
        /// </summary>
        public const string DisconnectSourceServer = "server_request";

        /// <summary>
        /// Receive loop exit reason: canceled (stop or dispose).
        /// </summary>
        public const string LoopExitCanceled = "canceled";

        /// <summary>
        /// Receive loop exit reason: disconnected with reconnect disabled.
        /// </summary>
        public const string LoopExitNoReconnect = "no_reconnect";

        /// <summary>
        /// Receive loop exit reason: an unhandled exception (for example, a failed reconnect or a throwing handler) ended the loop.
        /// </summary>
        public const string LoopExitFaulted = "faulted";

        /// <summary>
        /// Component: Slack Web API.
        /// </summary>
        public const string ComponentApi = "api";

        /// <summary>
        /// Component: Socket Mode connection.
        /// </summary>
        public const string ComponentSocket = "socket";

        /// <summary>
        /// Component: envelope pipeline.
        /// </summary>
        public const string ComponentEnvelope = "envelope";

        /// <summary>
        /// Component: application event handler.
        /// </summary>
        public const string ComponentHandler = "handler";

        /// <summary>
        /// Component: receive loop.
        /// </summary>
        public const string ComponentReceiveLoop = "receive_loop";

        /// <summary>
        /// Handler event: MessageReceived.
        /// </summary>
        public const string EventMessageReceived = "MessageReceived";

        /// <summary>
        /// Handler event: Connected.
        /// </summary>
        public const string EventConnected = "Connected";

        /// <summary>
        /// Handler event: Disconnected.
        /// </summary>
        public const string EventDisconnected = "Disconnected";

        /// <summary>
        /// Handler event: ActionRequired.
        /// </summary>
        public const string EventActionRequired = "ActionRequired";

        #endregion
    }
}
