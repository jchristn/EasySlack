namespace EasySlack.Internal
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Net.Http;
    using System.Reflection;
    using System.Threading;

    /// <summary>
    /// Owns the EasySlack <see cref="Meter"/> and <see cref="ActivitySource"/> and every instrument recorded on them.
    /// Recording is best-effort: no method here throws, so instrumentation can never break the connector.
    /// With no listener attached, spans are not created and instruments short-circuit, so the cost is negligible.
    /// This class is thread safe.
    /// </summary>
    internal static class EasySlackTelemetry
    {
        /// <summary>
        /// Gets the EasySlack library version reported on the meter, the activity source, and the build info gauge.
        /// </summary>
        internal static readonly string Version = ResolveVersion();

        /// <summary>
        /// Gets the activity source spans are opened on.
        /// </summary>
        internal static readonly ActivitySource Source = new ActivitySource(EasySlackTelemetryNames.ActivitySourceName, Version);

        /// <summary>
        /// Gets the meter instruments are recorded on.
        /// </summary>
        internal static readonly Meter Meter = new Meter(EasySlackTelemetryNames.MeterName, Version);

        private static readonly double[] _DurationBuckets = new double[] { 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10, 30, 60 };
        private static readonly double[] _SizeBuckets = new double[] { 256, 1024, 4096, 16384, 65536, 262144, 1048576 };

        private static readonly Histogram<double> _OperationDuration = CreateHistogram(EasySlackTelemetryNames.OperationDuration, "s", "Duration of a public EasySlack connector operation.", _DurationBuckets);
        private static readonly Counter<long> _Operations = Meter.CreateCounter<long>(EasySlackTelemetryNames.Operations, "{operation}", "Public EasySlack connector operations by outcome.");
        private static readonly Histogram<double> _ApiRequestDuration = CreateHistogram(EasySlackTelemetryNames.ApiRequestDuration, "s", "Duration of one Slack Web API call.", _DurationBuckets);
        private static readonly Counter<long> _ApiRequests = Meter.CreateCounter<long>(EasySlackTelemetryNames.ApiRequests, "{request}", "Slack Web API calls by method and outcome.");
        private static readonly Histogram<double> _SocketConnectDuration = CreateHistogram(EasySlackTelemetryNames.SocketConnectDuration, "s", "Duration of establishing a Socket Mode connection.", _DurationBuckets);
        private static readonly Counter<long> _SocketConnects = Meter.CreateCounter<long>(EasySlackTelemetryNames.SocketConnects, "{attempt}", "Socket Mode connection attempts by outcome.");
        private static readonly UpDownCounter<long> _SocketConnectionsActive = Meter.CreateUpDownCounter<long>(EasySlackTelemetryNames.SocketConnectionsActive, "{connection}", "Socket Mode connections currently connected.");
        private static readonly Counter<long> _SocketDisconnects = Meter.CreateCounter<long>(EasySlackTelemetryNames.SocketDisconnects, "{disconnect}", "Socket Mode disconnects by source.");
        private static readonly Histogram<double> _SocketReconnectDelay = CreateHistogram(EasySlackTelemetryNames.SocketReconnectDelay, "s", "Backoff delay applied before a reconnect attempt.", _DurationBuckets);
        private static readonly Counter<long> _ReceiveLoopExits = Meter.CreateCounter<long>(EasySlackTelemetryNames.ReceiveLoopExits, "{exit}", "Socket Mode receive loop terminations by reason.");
        private static readonly Histogram<double> _SocketMessageSize = CreateHistogram(EasySlackTelemetryNames.SocketMessageSize, "By", "Size of each inbound Socket Mode text message.", _SizeBuckets);
        private static readonly Counter<long> _Envelopes = Meter.CreateCounter<long>(EasySlackTelemetryNames.Envelopes, "{envelope}", "Socket Mode envelopes processed by type and outcome.");
        private static readonly Histogram<double> _EnvelopeDuration = CreateHistogram(EasySlackTelemetryNames.EnvelopeDuration, "s", "End-to-end processing duration of one Socket Mode envelope.", _DurationBuckets);
        private static readonly Histogram<double> _EnvelopeStageDuration = CreateHistogram(EasySlackTelemetryNames.EnvelopeStageDuration, "s", "Duration of one envelope pipeline stage.", _DurationBuckets);
        private static readonly Counter<long> _EnvelopeStageEvents = Meter.CreateCounter<long>(EasySlackTelemetryNames.EnvelopeStageEvents, "{event}", "Envelope pipeline stage executions by outcome.");
        private static readonly Counter<long> _EventsReceived = Meter.CreateCounter<long>(EasySlackTelemetryNames.EventsReceived, "{event}", "Events API events received by type.");
        private static readonly Counter<long> _Messages = Meter.CreateCounter<long>(EasySlackTelemetryNames.Messages, "{message}", "Inbound message events by disposition.");
        private static readonly Histogram<double> _HandlerDuration = CreateHistogram(EasySlackTelemetryNames.HandlerDuration, "s", "Time spent in application event handlers for one event raise.", _DurationBuckets);
        private static readonly Counter<long> _HandlerInvocations = Meter.CreateCounter<long>(EasySlackTelemetryNames.HandlerInvocations, "{invocation}", "Application event handler raises by outcome.");
        private static readonly Counter<long> _ActionRequired = Meter.CreateCounter<long>(EasySlackTelemetryNames.ActionRequired, "{event}", "ActionRequired conditions raised by code.");
        private static readonly Counter<long> _Errors = Meter.CreateCounter<long>(EasySlackTelemetryNames.Errors, "{error}", "EasySlack failures by component and error type.");
        private static readonly Counter<long> _StateTransitions = Meter.CreateCounter<long>(EasySlackTelemetryNames.StateTransitions, "{transition}", "Connector connection state transitions.");

        private static long _LastEnvelopeSuccessUnixMs = 0;

        static EasySlackTelemetry()
        {
            Meter.CreateObservableGauge<int>(
                EasySlackTelemetryNames.BuildInfo,
                ObserveBuildInfo,
                "{info}",
                "Always 1, labeled with the EasySlack library version.");

            Meter.CreateObservableGauge<double>(
                EasySlackTelemetryNames.EnvelopeLastSuccessTime,
                ObserveLastEnvelopeSuccess,
                "s",
                "Unix time of the most recent successfully processed Socket Mode envelope.");
        }

        #region Operations

        /// <summary>
        /// Starts a span for a public connector operation.
        /// </summary>
        /// <param name="operation">The operation name.</param>
        /// <returns>The span, or null when nobody is listening.</returns>
        internal static Activity? StartOperation(string operation)
        {
            Activity? activity = StartActivity(EasySlackTelemetryNames.SpanOperationPrefix + operation, ActivityKind.Internal);
            SetTag(activity, EasySlackTelemetryNames.AttributeOperation, operation);
            return activity;
        }

        /// <summary>
        /// Records a completed public connector operation.
        /// </summary>
        /// <param name="activity">The operation span.</param>
        /// <param name="operation">The operation name.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="outcome">The outcome.</param>
        /// <param name="errorType">The error type, or null on success.</param>
        internal static void CompleteOperation(Activity? activity, string operation, long started, string outcome, string? errorType)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(EasySlackTelemetryNames.AttributeOperation, operation);
                tags.Add(EasySlackTelemetryNames.AttributeOutcome, outcome);
                if (errorType != null) tags.Add(EasySlackTelemetryNames.AttributeErrorType, errorType);

                _OperationDuration.Record(Elapsed(started), tags);
                _Operations.Add(1, tags);
                SetOutcome(activity, outcome, errorType);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a completed public connector operation from a Slack result's ok flag and error code.
        /// </summary>
        /// <param name="activity">The operation span.</param>
        /// <param name="operation">The operation name.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="ok">The Slack ok flag.</param>
        /// <param name="slackError">The Slack error code, if any.</param>
        internal static void CompleteOperationResult(Activity? activity, string operation, long started, bool ok, string? slackError)
        {
            if (ok) CompleteOperation(activity, operation, started, EasySlackTelemetryNames.OutcomeSuccess, null);
            else CompleteOperation(activity, operation, started, EasySlackTelemetryNames.OutcomeSlackError, NormalizeCode(slackError));
        }

        /// <summary>
        /// Records a failed public connector operation. Intended for use in an exception filter.
        /// </summary>
        /// <param name="activity">The operation span.</param>
        /// <param name="operation">The operation name.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>Always false, so the exception filter never catches.</returns>
        internal static bool FailOperation(Activity? activity, string operation, long started, Exception exception)
        {
            RecordException(activity, exception);
            CompleteOperation(activity, operation, started, OutcomeFor(exception), ErrorTypeFor(exception));
            return false;
        }

        #endregion

        #region Slack-Web-API

        /// <summary>
        /// Starts a client span for one Slack Web API call.
        /// </summary>
        /// <param name="apiMethod">The Slack API method, for example chat.postMessage.</param>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <param name="baseAddress">The API base address.</param>
        /// <returns>The span, or null when nobody is listening.</returns>
        internal static Activity? StartApi(string apiMethod, string httpMethod, Uri? baseAddress)
        {
            Activity? activity = StartActivity(EasySlackTelemetryNames.SpanApiPrefix + apiMethod, ActivityKind.Client);
            SetTag(activity, EasySlackTelemetryNames.AttributeApiMethod, apiMethod);
            SetTag(activity, EasySlackTelemetryNames.AttributeHttpRequestMethod, httpMethod);
            if (baseAddress != null) SetTag(activity, EasySlackTelemetryNames.AttributeServerAddress, baseAddress.Host);
            return activity;
        }

        /// <summary>
        /// Records a completed Slack Web API call.
        /// </summary>
        /// <param name="activity">The client span.</param>
        /// <param name="apiMethod">The Slack API method.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="statusCode">The HTTP status code, or 0 when no response was received.</param>
        /// <param name="outcome">The outcome.</param>
        /// <param name="errorType">The error type, or null on success.</param>
        internal static void CompleteApi(Activity? activity, string apiMethod, long started, int statusCode, string outcome, string? errorType)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(EasySlackTelemetryNames.AttributeApiMethod, apiMethod);
                tags.Add(EasySlackTelemetryNames.AttributeOutcome, outcome);
                if (statusCode > 0) tags.Add(EasySlackTelemetryNames.AttributeHttpStatusCode, statusCode);
                if (errorType != null) tags.Add(EasySlackTelemetryNames.AttributeErrorType, errorType);

                _ApiRequestDuration.Record(Elapsed(started), tags);
                _ApiRequests.Add(1, tags);
                if (statusCode > 0) SetTag(activity, EasySlackTelemetryNames.AttributeHttpStatusCode, statusCode);
                SetOutcome(activity, outcome, errorType);
                if (errorType != null && outcome != EasySlackTelemetryNames.OutcomeCanceled) RecordError(EasySlackTelemetryNames.ComponentApi, errorType);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a Slack Web API call that threw. Intended for use in an exception filter.
        /// </summary>
        /// <param name="activity">The client span.</param>
        /// <param name="apiMethod">The Slack API method.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="statusCode">The HTTP status code, or 0 when no response was received.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>Always false, so the exception filter never catches.</returns>
        internal static bool FailApi(Activity? activity, string apiMethod, long started, int statusCode, Exception exception)
        {
            RecordException(activity, exception);
            CompleteApi(activity, apiMethod, started, statusCode, OutcomeFor(exception), ErrorTypeFor(exception));
            return false;
        }

        /// <summary>
        /// Returns the bounded Slack API method from a relative request path by removing any query string.
        /// </summary>
        /// <param name="relativePath">The relative request path.</param>
        /// <returns>The API method.</returns>
        internal static string ApiMethodFromPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return EasySlackTelemetryNames.TypeOther;
            int index = relativePath.IndexOf('?');
            return index < 0 ? relativePath : relativePath.Substring(0, index);
        }

        #endregion

        #region Socket-Mode

        /// <summary>
        /// Starts a client span for a Socket Mode connection attempt.
        /// </summary>
        /// <param name="isReconnect">Whether the attempt is a reconnect.</param>
        /// <returns>The span, or null when nobody is listening.</returns>
        internal static Activity? StartSocketConnect(bool isReconnect)
        {
            Activity? activity = StartActivity(EasySlackTelemetryNames.SpanSocketConnect, ActivityKind.Client);
            SetTag(activity, EasySlackTelemetryNames.AttributeReconnect, isReconnect);
            return activity;
        }

        /// <summary>
        /// Records a completed Socket Mode connection attempt.
        /// </summary>
        /// <param name="activity">The connect span.</param>
        /// <param name="isReconnect">Whether the attempt is a reconnect.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="outcome">The outcome.</param>
        /// <param name="errorType">The error type, or null on success.</param>
        internal static void CompleteSocketConnect(Activity? activity, bool isReconnect, long started, string outcome, string? errorType)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(EasySlackTelemetryNames.AttributeReconnect, isReconnect);
                tags.Add(EasySlackTelemetryNames.AttributeOutcome, outcome);
                if (errorType != null) tags.Add(EasySlackTelemetryNames.AttributeErrorType, errorType);

                _SocketConnectDuration.Record(Elapsed(started), tags);
                _SocketConnects.Add(1, tags);
                SetOutcome(activity, outcome, errorType);
                if (errorType != null && outcome != EasySlackTelemetryNames.OutcomeCanceled) RecordError(EasySlackTelemetryNames.ComponentSocket, errorType);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a Socket Mode connection attempt that threw. Intended for use in an exception filter.
        /// </summary>
        /// <param name="activity">The connect span.</param>
        /// <param name="isReconnect">Whether the attempt is a reconnect.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>Always false, so the exception filter never catches.</returns>
        internal static bool FailSocketConnect(Activity? activity, bool isReconnect, long started, Exception exception)
        {
            RecordException(activity, exception);
            CompleteSocketConnect(activity, isReconnect, started, OutcomeFor(exception), ErrorTypeFor(exception));
            return false;
        }

        /// <summary>
        /// Records a connection state transition and keeps the active connection gauge balanced.
        /// </summary>
        /// <param name="previous">The previous state.</param>
        /// <param name="next">The new state.</param>
        internal static void RecordStateTransition(SlackConnectionState previous, SlackConnectionState next)
        {
            if (previous == next) return;

            try
            {
                _StateTransitions.Add(1, new KeyValuePair<string, object?>(EasySlackTelemetryNames.AttributeConnectionState, StateName(next)));
                if (next == SlackConnectionState.Connected) _SocketConnectionsActive.Add(1);
                else if (previous == SlackConnectionState.Connected) _SocketConnectionsActive.Add(-1);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Starts a span covering a disconnect and any reconnect that follows. It is a trace root for transport
        /// disconnects (raised from the receive loop) and a child of the envelope span for Slack disconnect envelopes.
        /// </summary>
        /// <param name="source">The disconnect source.</param>
        /// <param name="reason">The free-form reason (span only).</param>
        /// <param name="willReconnect">Whether the connector will reconnect.</param>
        /// <param name="connection">The context of the span that established the lost connection, linked for navigation.</param>
        /// <returns>The span, or null when nobody is listening.</returns>
        internal static Activity? StartDisconnect(string source, string? reason, bool willReconnect, ActivityContext connection)
        {
            Activity? activity = StartLinkedActivity(EasySlackTelemetryNames.SpanDisconnect, ActivityKind.Internal, connection);
            SetTag(activity, EasySlackTelemetryNames.AttributeDisconnectSource, source);
            SetTag(activity, EasySlackTelemetryNames.AttributeDisconnectReason, reason);
            SetTag(activity, EasySlackTelemetryNames.AttributeWillReconnect, willReconnect);
            return activity;
        }

        /// <summary>
        /// Records a disconnect.
        /// </summary>
        /// <param name="source">The disconnect source.</param>
        /// <param name="willReconnect">Whether the connector will reconnect.</param>
        internal static void RecordDisconnect(string source, bool willReconnect)
        {
            try
            {
                _SocketDisconnects.Add(
                    1,
                    new KeyValuePair<string, object?>(EasySlackTelemetryNames.AttributeDisconnectSource, source),
                    new KeyValuePair<string, object?>(EasySlackTelemetryNames.AttributeWillReconnect, willReconnect));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records the backoff delay applied before a reconnect attempt.
        /// </summary>
        /// <param name="activity">The disconnect span.</param>
        /// <param name="delayMs">The delay in milliseconds.</param>
        internal static void RecordReconnectDelay(Activity? activity, int delayMs)
        {
            try
            {
                double seconds = delayMs / 1000.0;
                _SocketReconnectDelay.Record(seconds);
                SetTag(activity, EasySlackTelemetryNames.AttributeReconnectDelay, seconds);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a receive loop exit.
        /// </summary>
        /// <param name="reason">The exit reason.</param>
        /// <param name="errorType">The error type when faulted, otherwise null.</param>
        internal static void RecordLoopExit(string reason, string? errorType)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(EasySlackTelemetryNames.AttributeLoopExitReason, reason);
                if (errorType != null) tags.Add(EasySlackTelemetryNames.AttributeErrorType, errorType);
                _ReceiveLoopExits.Add(1, tags);
                if (errorType != null) RecordError(EasySlackTelemetryNames.ComponentReceiveLoop, errorType);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a receive loop fault. Intended for use in an exception filter.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <returns>Always false, so the exception filter never catches.</returns>
        internal static bool FailLoop(Exception exception)
        {
            RecordLoopExit(EasySlackTelemetryNames.LoopExitFaulted, ErrorTypeFor(exception));
            return false;
        }

        /// <summary>
        /// Records the size of one inbound Socket Mode message.
        /// </summary>
        /// <param name="bytes">The size in bytes.</param>
        internal static void RecordMessageSize(long bytes)
        {
            try
            {
                _SocketMessageSize.Record(bytes);
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Envelope-Pipeline

        /// <summary>
        /// Starts the consumer span for one inbound envelope. Inside the receive loop the span starts a new trace and
        /// links to the span that established the connection, so a long-lived connection does not become one unbounded trace.
        /// </summary>
        /// <param name="sizeBytes">The approximate payload size.</param>
        /// <param name="connection">The context of the connect span to link, or default.</param>
        /// <returns>The span, or null when nobody is listening.</returns>
        internal static Activity? StartEnvelope(long sizeBytes, ActivityContext connection)
        {
            Activity? activity = StartLinkedActivity(EasySlackTelemetryNames.SpanEnvelope, ActivityKind.Consumer, connection);
            SetTag(activity, EasySlackTelemetryNames.AttributeMessageSize, sizeBytes);
            return activity;
        }

        /// <summary>
        /// Records a completed envelope (the pipeline job).
        /// </summary>
        /// <param name="activity">The envelope span.</param>
        /// <param name="envelopeType">The normalized envelope type.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="outcome">The outcome.</param>
        /// <param name="errorType">The error type, or null on success.</param>
        internal static void CompleteEnvelope(Activity? activity, string envelopeType, long started, string outcome, string? errorType)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(EasySlackTelemetryNames.AttributeEnvelopeType, envelopeType);
                tags.Add(EasySlackTelemetryNames.AttributeOutcome, outcome);
                if (errorType != null) tags.Add(EasySlackTelemetryNames.AttributeErrorType, errorType);

                _EnvelopeDuration.Record(Elapsed(started), tags);
                _Envelopes.Add(1, tags);
                SetTag(activity, EasySlackTelemetryNames.AttributeEnvelopeType, envelopeType);
                SetOutcome(activity, outcome, errorType);

                if (outcome == EasySlackTelemetryNames.OutcomeSuccess)
                {
                    Interlocked.Exchange(ref _LastEnvelopeSuccessUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                }
                else if (errorType != null && outcome != EasySlackTelemetryNames.OutcomeCanceled)
                {
                    RecordError(EasySlackTelemetryNames.ComponentEnvelope, errorType);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records an envelope that threw. Intended for use in an exception filter.
        /// </summary>
        /// <param name="activity">The envelope span.</param>
        /// <param name="envelopeType">The normalized envelope type.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>Always false, so the exception filter never catches.</returns>
        internal static bool FailEnvelope(Activity? activity, string envelopeType, long started, Exception exception)
        {
            RecordException(activity, exception);
            CompleteEnvelope(activity, envelopeType, started, OutcomeFor(exception), ErrorTypeFor(exception));
            return false;
        }

        /// <summary>
        /// Starts a span for one envelope pipeline stage.
        /// </summary>
        /// <param name="stage">The stage name.</param>
        /// <returns>The span, or null when nobody is listening.</returns>
        internal static Activity? StartStage(string stage)
        {
            Activity? activity = StartActivity(EasySlackTelemetryNames.SpanStagePrefix + stage, ActivityKind.Internal);
            SetTag(activity, EasySlackTelemetryNames.AttributeStage, stage);
            return activity;
        }

        /// <summary>
        /// Records a completed envelope pipeline stage.
        /// </summary>
        /// <param name="activity">The stage span.</param>
        /// <param name="stage">The stage name.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="outcome">The outcome.</param>
        /// <param name="errorType">The error type, or null on success.</param>
        internal static void CompleteStage(Activity? activity, string stage, long started, string outcome, string? errorType)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(EasySlackTelemetryNames.AttributeStage, stage);
                tags.Add(EasySlackTelemetryNames.AttributeOutcome, outcome);
                if (errorType != null) tags.Add(EasySlackTelemetryNames.AttributeErrorType, errorType);

                _EnvelopeStageDuration.Record(Elapsed(started), tags);
                _EnvelopeStageEvents.Add(1, tags);
                SetOutcome(activity, outcome, errorType);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records an envelope pipeline stage that threw. Intended for use in an exception filter.
        /// </summary>
        /// <param name="activity">The stage span.</param>
        /// <param name="stage">The stage name.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>Always false, so the exception filter never catches.</returns>
        internal static bool FailStage(Activity? activity, string stage, long started, Exception exception)
        {
            RecordException(activity, exception);
            CompleteStage(activity, stage, started, OutcomeFor(exception), ErrorTypeFor(exception));
            return false;
        }

        /// <summary>
        /// Records an Events API event received inside an events_api envelope.
        /// </summary>
        /// <param name="eventType">The normalized event type.</param>
        internal static void RecordEventReceived(string eventType)
        {
            try
            {
                _EventsReceived.Add(1, new KeyValuePair<string, object?>(EasySlackTelemetryNames.AttributeEventType, eventType));
                SetTag(Activity.Current, EasySlackTelemetryNames.AttributeEventType, eventType);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records the disposition of an inbound message event.
        /// </summary>
        /// <param name="disposition">The disposition.</param>
        internal static void RecordMessageDisposition(string disposition)
        {
            try
            {
                _Messages.Add(1, new KeyValuePair<string, object?>(EasySlackTelemetryNames.AttributeMessageDisposition, disposition));
                SetTag(Activity.Current, EasySlackTelemetryNames.AttributeMessageDisposition, disposition);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records an ActionRequired condition.
        /// </summary>
        /// <param name="code">The library-defined action code.</param>
        internal static void RecordActionRequired(string? code)
        {
            try
            {
                string bounded = NormalizeCode(code);
                _ActionRequired.Add(1, new KeyValuePair<string, object?>(EasySlackTelemetryNames.AttributeActionCode, bounded));
                SetTag(Activity.Current, EasySlackTelemetryNames.AttributeActionCode, bounded);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Normalizes a Socket Mode envelope type to a bounded value.
        /// </summary>
        /// <param name="type">The raw type.</param>
        /// <returns>The bounded type.</returns>
        internal static string NormalizeEnvelopeType(string? type)
        {
            if (string.Equals(type, EasySlackTelemetryNames.EnvelopeTypeEventsApi, StringComparison.OrdinalIgnoreCase)) return EasySlackTelemetryNames.EnvelopeTypeEventsApi;
            if (string.Equals(type, EasySlackTelemetryNames.EnvelopeTypeDisconnect, StringComparison.OrdinalIgnoreCase)) return EasySlackTelemetryNames.EnvelopeTypeDisconnect;
            if (string.Equals(type, EasySlackTelemetryNames.EnvelopeTypeHello, StringComparison.OrdinalIgnoreCase)) return EasySlackTelemetryNames.EnvelopeTypeHello;
            return EasySlackTelemetryNames.TypeOther;
        }

        /// <summary>
        /// Normalizes an Events API event type to a bounded value.
        /// </summary>
        /// <param name="type">The raw type.</param>
        /// <returns>The bounded type.</returns>
        internal static string NormalizeEventType(string? type)
        {
            if (string.Equals(type, EasySlackTelemetryNames.EventTypeMessage, StringComparison.OrdinalIgnoreCase)) return EasySlackTelemetryNames.EventTypeMessage;
            if (string.Equals(type, EasySlackTelemetryNames.EventTypeAppRateLimited, StringComparison.OrdinalIgnoreCase)) return EasySlackTelemetryNames.EventTypeAppRateLimited;
            return EasySlackTelemetryNames.TypeOther;
        }

        #endregion

        #region Handlers

        /// <summary>
        /// Starts a span for raising one connector event to application handlers.
        /// </summary>
        /// <param name="eventName">The connector event name.</param>
        /// <param name="handlerCount">The number of subscribed handlers.</param>
        /// <returns>The span, or null when nobody is listening.</returns>
        internal static Activity? StartHandler(string eventName, int handlerCount)
        {
            Activity? activity = StartActivity(EasySlackTelemetryNames.SpanHandlerPrefix + eventName, ActivityKind.Internal);
            SetTag(activity, EasySlackTelemetryNames.AttributeEvent, eventName);
            SetTag(activity, EasySlackTelemetryNames.AttributeHandlerCount, handlerCount);
            return activity;
        }

        /// <summary>
        /// Records a completed handler raise.
        /// </summary>
        /// <param name="activity">The handler span.</param>
        /// <param name="eventName">The connector event name.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="outcome">The outcome.</param>
        /// <param name="errorType">The error type, or null on success.</param>
        internal static void CompleteHandler(Activity? activity, string eventName, long started, string outcome, string? errorType)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(EasySlackTelemetryNames.AttributeEvent, eventName);
                tags.Add(EasySlackTelemetryNames.AttributeOutcome, outcome);
                if (errorType != null) tags.Add(EasySlackTelemetryNames.AttributeErrorType, errorType);

                _HandlerDuration.Record(Elapsed(started), tags);
                _HandlerInvocations.Add(1, tags);
                SetOutcome(activity, outcome, errorType);
                if (errorType != null && outcome != EasySlackTelemetryNames.OutcomeCanceled) RecordError(EasySlackTelemetryNames.ComponentHandler, errorType);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Records a handler raise that threw. Intended for use in an exception filter.
        /// </summary>
        /// <param name="activity">The handler span.</param>
        /// <param name="eventName">The connector event name.</param>
        /// <param name="started">The <see cref="Stopwatch.GetTimestamp"/> value at start.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>Always false, so the exception filter never catches.</returns>
        internal static bool FailHandler(Activity? activity, string eventName, long started, Exception exception)
        {
            RecordException(activity, exception);
            CompleteHandler(activity, eventName, started, OutcomeFor(exception), ErrorTypeFor(exception));
            return false;
        }

        #endregion

        #region Shared

        /// <summary>
        /// Sets a tag on a span when the span exists and is recording.
        /// </summary>
        /// <param name="activity">The span.</param>
        /// <param name="key">The attribute key.</param>
        /// <param name="value">The attribute value.</param>
        internal static void SetTag(Activity? activity, string key, object? value)
        {
            if (activity == null || !activity.IsAllDataRequested || value == null) return;

            try
            {
                activity.SetTag(key, value);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Returns the bounded outcome for an exception.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The outcome.</returns>
        internal static string OutcomeFor(Exception exception)
        {
            if (exception is OperationCanceledException) return EasySlackTelemetryNames.OutcomeCanceled;
            if (exception is HttpRequestException) return EasySlackTelemetryNames.OutcomeHttpError;
            return EasySlackTelemetryNames.OutcomeError;
        }

        /// <summary>
        /// Returns the OpenTelemetry error type for an exception: its full type name.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The error type.</returns>
        internal static string ErrorTypeFor(Exception exception)
        {
            return exception.GetType().FullName ?? exception.GetType().Name;
        }

        /// <summary>
        /// Normalizes a Slack error code or library code to a bounded, label-safe value. Slack error codes are a
        /// documented finite set of lower-case snake_case identifiers. Anything else collapses to "other".
        /// </summary>
        /// <param name="code">The raw code.</param>
        /// <returns>The bounded code.</returns>
        internal static string NormalizeCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "unknown_error";
            if (code.Length > 64) return EasySlackTelemetryNames.TypeOther;

            foreach (char c in code)
            {
                bool allowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.';
                if (!allowed) return EasySlackTelemetryNames.TypeOther;
            }

            return code;
        }

        /// <summary>
        /// Starts a span linked to another span. The span is parented to <see cref="Activity.Current"/> when one exists
        /// and is a new trace root otherwise. The receive loop clears <see cref="Activity.Current"/> when it starts, so
        /// envelope and transport-disconnect spans begin their own traces instead of nesting under the connect span.
        /// </summary>
        /// <param name="name">The span name.</param>
        /// <param name="kind">The span kind.</param>
        /// <param name="link">The span context to link, or default for none.</param>
        /// <returns>The span, or null when nobody is listening.</returns>
        internal static Activity? StartLinkedActivity(string name, ActivityKind kind, ActivityContext link)
        {
            try
            {
                if (!Source.HasListeners()) return null;
                ActivityLink[]? links = link == default ? null : new ActivityLink[] { new ActivityLink(link) };
                return Source.StartActivity(name, kind, default(ActivityContext), null, links);
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private static Activity? StartActivity(string name, ActivityKind kind)
        {
            try
            {
                return Source.StartActivity(name, kind);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void SetOutcome(Activity? activity, string outcome, string? errorType)
        {
            if (activity == null) return;

            try
            {
                SetTag(activity, EasySlackTelemetryNames.AttributeOutcome, outcome);

                if (outcome == EasySlackTelemetryNames.OutcomeSuccess || outcome == EasySlackTelemetryNames.OutcomeSkipped)
                {
                    activity.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    SetTag(activity, EasySlackTelemetryNames.AttributeErrorType, errorType);
                    activity.SetStatus(ActivityStatusCode.Error, errorType ?? outcome);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void RecordException(Activity? activity, Exception exception)
        {
            if (activity == null || !activity.IsAllDataRequested) return;

            try
            {
                ActivityTagsCollection tags = new ActivityTagsCollection();
                tags.Add("exception.type", ErrorTypeFor(exception));
                tags.Add("exception.message", exception.Message);
                tags.Add("exception.stacktrace", exception.ToString());
                activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, tags));
            }
            catch (Exception)
            {
            }
        }

        private static void RecordError(string component, string errorType)
        {
            _Errors.Add(
                1,
                new KeyValuePair<string, object?>(EasySlackTelemetryNames.AttributeComponent, component),
                new KeyValuePair<string, object?>(EasySlackTelemetryNames.AttributeErrorType, errorType));
        }

        private static double Elapsed(long started)
        {
            return Stopwatch.GetElapsedTime(started).TotalSeconds;
        }

        private static string StateName(SlackConnectionState state)
        {
            switch (state)
            {
                case SlackConnectionState.Disconnected: return "disconnected";
                case SlackConnectionState.Connecting: return "connecting";
                case SlackConnectionState.Connected: return "connected";
                case SlackConnectionState.Stopping: return "stopping";
                default: return EasySlackTelemetryNames.TypeOther;
            }
        }

        private static Histogram<double> CreateHistogram(string name, string unit, string description, double[] buckets)
        {
#if NET9_0_OR_GREATER
            InstrumentAdvice<double> advice = new InstrumentAdvice<double> { HistogramBucketBoundaries = buckets };
            return Meter.CreateHistogram<double>(name, unit, description, null, advice);
#else
            return Meter.CreateHistogram<double>(name, unit, description);
#endif
        }

        private static Measurement<int> ObserveBuildInfo()
        {
            return new Measurement<int>(1, new KeyValuePair<string, object?>(EasySlackTelemetryNames.AttributeVersion, Version));
        }

        private static IEnumerable<Measurement<double>> ObserveLastEnvelopeSuccess()
        {
            long value = Interlocked.Read(ref _LastEnvelopeSuccessUnixMs);
            if (value <= 0) return Array.Empty<Measurement<double>>();
            return new Measurement<double>[] { new Measurement<double>(value / 1000.0) };
        }

        private static string ResolveVersion()
        {
            try
            {
                Assembly assembly = typeof(EasySlackTelemetry).Assembly;
                AssemblyInformationalVersionAttribute? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string? version = informational?.InformationalVersion;
                if (string.IsNullOrWhiteSpace(version)) version = assembly.GetName().Version?.ToString();
                if (string.IsNullOrWhiteSpace(version)) return "unknown";

                int plus = version.IndexOf('+');
                return plus < 0 ? version : version.Substring(0, plus);
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        #endregion
    }
}
