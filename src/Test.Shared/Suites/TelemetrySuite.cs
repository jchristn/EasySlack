namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using EasySlack;
    using EasySlack.Internal;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Proves EasySlack emits its documented metrics and spans for every instrumented category: public operations,
    /// Slack Web API calls, Socket Mode connect/disconnect/reconnect, the envelope pipeline and its stages,
    /// application handlers, lifecycle state, and build info, including failure paths and the no-listener path.
    /// Listeners are process-wide, so assertions check presence only, using unique values where possible.
    /// </summary>
    public static class TelemetrySuite
    {
        private const string SuiteId = "Telemetry";
        private const string SocketOpenResponse = "{\"ok\":true,\"url\":\"wss://example.test/socket\"}";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("SourceNamesAreStable", "Meter and activity source names are the documented public contract", ct =>
                {
                    Check.Equal("EasySlack", EasySlackTelemetryNames.MeterName, "meter name");
                    Check.Equal("EasySlack", EasySlackTelemetryNames.ActivitySourceName, "activity source name");
                    Check.Equal(EasySlackTelemetryNames.MeterName, EasySlackTelemetry.Meter.Name, "live meter name");
                    Check.Equal(EasySlackTelemetryNames.ActivitySourceName, EasySlackTelemetry.Source.Name, "live source name");
                    return Task.CompletedTask;
                }),

                Case("NoListenerDoesNotThrow", "Instrumented paths run cleanly with no listener attached", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":true}");
                    h.Http.EnqueueJson("{\"ok\":false,\"error\":\"invalid_auth\"}");

                    SlackValidationResult ok = await h.Connector.ValidateConnectionAsync(ct).ConfigureAwait(false);
                    SlackValidationResult failed = await h.Connector.ValidateConnectionAsync(ct).ConfigureAwait(false);
                    await h.Connector.ProcessSocketMessageAsync("{\"envelope_id\":\"nl\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"channel\":\"C1\",\"ts\":\"1.1\"}}}", ct).ConfigureAwait(false);
                    await Check.ThrowsAsync<JsonException>(() => h.Connector.ProcessSocketMessageAsync("{nope", ct), "malformed still throws").ConfigureAwait(false);

                    Check.True(ok.Ok, "validate ok");
                    Check.False(failed.Ok, "validate failure surfaced");
                }),

                Case("ApiSuccessEmitsSpansAndMetrics", "A successful API call emits a client span under the operation span plus metrics", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":true,\"team\":\"T\"}");

                    ActivityTraceId testTrace;
                    using (Activity root = capture.StartTestTrace())
                    {
                        testTrace = root.TraceId;
                        await h.Connector.ValidateConnectionAsync(ct).ConfigureAwait(false);
                    }

                    Activity operation = capture.SpansInTrace(testTrace).Single(a => a.DisplayName == "easyslack validate_connection");
                    Activity client = capture.SpansInTrace(operation.TraceId).Single(a => a.DisplayName == "slack auth.test");
                    Check.Equal(ActivityKind.Client, client.Kind, "client span kind");
                    Check.Equal(operation.SpanId, client.ParentSpanId, "client span parented to operation span");
                    Check.Equal(ActivityStatusCode.Ok, client.Status, "client span status");
                    Check.Equal(ActivityStatusCode.Ok, operation.Status, "operation span status");
                    Check.Equal("auth.test", (string?)client.GetTagItem(EasySlackTelemetryNames.AttributeApiMethod), "api method tag");
                    Check.Equal("slack.com", (string?)client.GetTagItem(EasySlackTelemetryNames.AttributeServerAddress), "server address tag");
                    Check.Equal(200, (int)client.GetTagItem(EasySlackTelemetryNames.AttributeHttpStatusCode)!, "status code tag");

                    capture.Require(EasySlackTelemetryNames.ApiRequests, T("slack.api.method", "auth.test", "easyslack.outcome", "success", "http.response.status_code", "200"));
                    List<CapturedMeasurement> durations = capture.Require(EasySlackTelemetryNames.ApiRequestDuration, T("slack.api.method", "auth.test", "easyslack.outcome", "success"));
                    Check.Equal("s", durations[0].Unit, "duration unit");
                    Check.True(durations.All(d => d.Value >= 0), "non-negative duration");
                    capture.Require(EasySlackTelemetryNames.Operations, T("easyslack.operation", "validate_connection", "easyslack.outcome", "success"));
                    capture.Require(EasySlackTelemetryNames.OperationDuration, T("easyslack.operation", "validate_connection", "easyslack.outcome", "success"));
                    Check.False(capture.Find(EasySlackTelemetryNames.ApiRequests, T("slack.api.method", "auth.test", "easyslack.outcome", "success")).Any(m => m.Tags.ContainsKey("error.type")), "no error.type on success");
                }),

                Case("ApiSlackErrorRecorded", "An ok:false response records slack_error with the Slack error code", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":false,\"error\":\"telemetry_test_auth\"}");

                    SlackValidationResult result = await h.Connector.ValidateConnectionAsync(ct).ConfigureAwait(false);
                    Check.False(result.Ok, "result not ok");

                    capture.Require(EasySlackTelemetryNames.ApiRequests, T("slack.api.method", "auth.test", "easyslack.outcome", "slack_error", "error.type", "telemetry_test_auth"));
                    capture.Require(EasySlackTelemetryNames.Operations, T("easyslack.operation", "validate_connection", "easyslack.outcome", "slack_error", "error.type", "telemetry_test_auth"));
                    capture.Require(EasySlackTelemetryNames.Errors, T("easyslack.component", "api", "error.type", "telemetry_test_auth"));

                    Activity client = capture.RequireSpan("slack auth.test", "error.type", "telemetry_test_auth");
                    Check.Equal(ActivityStatusCode.Error, client.Status, "client span error status");
                    Check.Equal("telemetry_test_auth", client.StatusDescription, "status description");
                }),

                Case("ApiHttpErrorRecorded", "A non-success HTTP status records http_error and the exception on the operation span", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(_ => StubHttpMessageHandler.CreateJsonResponse("{}", (HttpStatusCode)503));

                    await Check.ThrowsAsync<HttpRequestException>(() => h.Connector.ValidateConnectionAsync(ct), "http error").ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.ApiRequests, T("slack.api.method", "auth.test", "easyslack.outcome", "http_error", "http.response.status_code", "503", "error.type", "503"));
                    capture.Require(EasySlackTelemetryNames.Operations, T("easyslack.operation", "validate_connection", "easyslack.outcome", "http_error", "error.type", "System.Net.Http.HttpRequestException"));
                    capture.Require(EasySlackTelemetryNames.Errors, T("easyslack.component", "api", "error.type", "503"));

                    Activity operation = capture.RequireSpan("easyslack validate_connection", "error.type", "System.Net.Http.HttpRequestException");
                    Check.Equal(ActivityStatusCode.Error, operation.Status, "operation span error status");
                    Check.True(operation.Events.Any(e => e.Name == "exception"), "exception event recorded");
                    Check.Equal(1, capture.SpansInTrace(operation.TraceId).Count(a => a.DisplayName == "slack auth.test"), "API call recorded exactly once");
                }),

                Case("ApiRateLimitRecordsRetryAfter", "A 429 records http_error with status 429 and the Retry-After span attribute", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(_ =>
                    {
                        HttpResponseMessage response = StubHttpMessageHandler.CreateJsonResponse("{\"ok\":false,\"error\":\"ratelimited\"}", (HttpStatusCode)429);
                        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                        return response;
                    });

                    await Check.ThrowsAsync<HttpRequestException>(() => h.Connector.GetUserInfoAsync("U-RL", ct), "rate limited").ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.ApiRequests, T("slack.api.method", "users.info", "easyslack.outcome", "http_error", "http.response.status_code", "429"));
                    Activity client = capture.RequireSpan("slack users.info", "http.response.status_code", "429");
                    Check.Equal(7d, (double)client.GetTagItem(EasySlackTelemetryNames.AttributeRetryAfter)!, "retry after");
                }),

                Case("ApiMethodLabelExcludesQuery", "Query-string API calls are labeled by method only and carry ids on spans only", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":true,\"channel\":{\"id\":\"C-TELEMETRY\"}}");

                    await h.Connector.GetChannelInfoAsync("C-TELEMETRY", ct).ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.ApiRequests, T("slack.api.method", "conversations.info", "easyslack.outcome", "success"));
                    Check.False(capture.Find(EasySlackTelemetryNames.ApiRequests).Any(m => Convert.ToString(m.Tags["slack.api.method"])!.Contains('?')), "no query string in label");
                    Check.False(capture.Measurements.Any(m => m.Tags.Values.Any(v => Convert.ToString(v) == "C-TELEMETRY")), "channel id never on a metric");
                    capture.RequireSpan("easyslack get_channel_info", "slack.channel.id", "C-TELEMETRY");
                }),

                Case("SendToUserTracesBothCalls", "SendMessageToUser traces conversations.open and chat.postMessage under one operation span", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":true,\"channel\":{\"id\":\"D-TELEMETRY\"}}");
                    h.Http.EnqueueJson("{\"ok\":true,\"channel\":\"D-TELEMETRY\",\"ts\":\"1.2\"}");

                    await h.Connector.SendMessageToUserAsync("U-TELEMETRY", "secret text", ct).ConfigureAwait(false);

                    Activity operation = capture.RequireSpan("easyslack send_message_to_user", "slack.channel.id", "D-TELEMETRY");
                    List<Activity> trace = capture.SpansInTrace(operation.TraceId);
                    Check.True(trace.Any(a => a.DisplayName == "slack conversations.open" && a.ParentSpanId == operation.SpanId), "conversations.open child");
                    Check.True(trace.Any(a => a.DisplayName == "slack chat.postMessage" && a.ParentSpanId == operation.SpanId), "chat.postMessage child");
                    Check.False(trace.Any(a => a.DisplayName == "easyslack send_message_to_channel"), "no nested public operation span");
                    Check.False(trace.Any(a => a.TagObjects.Any(t => Convert.ToString(t.Value) == "secret text")), "message text never on a span");
                    capture.Require(EasySlackTelemetryNames.Operations, T("easyslack.operation", "send_message_to_user", "easyslack.outcome", "success"));
                }),

                Case("ArgumentFailureRecorded", "An argument validation failure records outcome error with the exception type", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();

                    await Check.ThrowsAsync<ArgumentNullException>(() => h.Connector.SendMessageToChannelAsync("C1", "  ", null, ct), "blank text").ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.Operations, T("easyslack.operation", "send_message_to_channel", "easyslack.outcome", "error", "error.type", "System.ArgumentNullException"));
                }),

                Case("CancellationRecorded", "A canceled call records outcome canceled", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(_ => throw new OperationCanceledException("simulated cancellation"));

                    await Check.ThrowsAsync<OperationCanceledException>(() => h.Connector.GetUserInfoAsync("U-CANCEL", ct), "canceled").ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.ApiRequests, T("slack.api.method", "users.info", "easyslack.outcome", "canceled"));
                    capture.Require(EasySlackTelemetryNames.Operations, T("easyslack.operation", "get_user_info", "easyslack.outcome", "canceled"));
                }),

                Case("SocketLifecycleAndEnvelopePipeline", "Start, an inbound message, and stop emit connect, pipeline, stage, handler, and lifecycle telemetry", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Socket.KeepOpenWhenDrained = true;
                    h.Http.EnqueueJson(SocketOpenResponse);

                    int received = 0;
                    h.Connector.MessageReceived += (sender, args) =>
                    {
                        Interlocked.Increment(ref received);
                        return Task.CompletedTask;
                    };

                    string envelopeId = "env-" + Guid.NewGuid().ToString("N");
                    h.Socket.EnqueueIncomingText("{\"envelope_id\":\"" + envelopeId + "\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"channel\":\"C1\",\"user\":\"U1\",\"text\":\"hello\",\"ts\":\"1.1\",\"thread_ts\":\"1.0\"}}}");

                    ActivityTraceId testTrace;
                    using (Activity root = capture.StartTestTrace())
                    {
                        testTrace = root.TraceId;
                        await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    }

                    await capture.WaitForAsync(() => capture.SpansNamed("slack socket_mode.envelope").Any(a => (string?)a.GetTagItem("slack.envelope.id") == envelopeId), ct).ConfigureAwait(false);
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);

                    Check.Equal(1, received, "message handled");

                    Activity start = capture.SpansInTrace(testTrace).Single(a => a.DisplayName == "easyslack start");
                    Activity connect = capture.SpansInTrace(start.TraceId).Single(a => a.DisplayName == "slack socket_mode.connect");
                    Check.Equal(start.SpanId, connect.ParentSpanId, "connect under start");
                    Check.True(capture.SpansInTrace(start.TraceId).Any(a => a.DisplayName == "slack apps.connections.open" && a.ParentSpanId == connect.SpanId), "apps.connections.open under connect");

                    Activity envelope = capture.RequireSpan("slack socket_mode.envelope", "slack.envelope.id", envelopeId);
                    Check.Equal(ActivityKind.Consumer, envelope.Kind, "envelope span kind");
                    Check.True(envelope.TraceId != start.TraceId, "envelope starts its own trace");
                    Check.True(envelope.Links.Any(l => l.Context.SpanId == connect.SpanId), "envelope links to connect span");
                    Check.Equal("events_api", (string?)envelope.GetTagItem(EasySlackTelemetryNames.AttributeEnvelopeType), "envelope type tag");

                    List<Activity> envelopeTrace = capture.SpansInTrace(envelope.TraceId);
                    Activity dispatch = envelopeTrace.Single(a => a.DisplayName == "stage:dispatch");
                    Check.True(envelopeTrace.Any(a => a.DisplayName == "stage:parse" && a.ParentSpanId == envelope.SpanId), "parse stage span");
                    Check.True(envelopeTrace.Any(a => a.DisplayName == "stage:ack" && a.ParentSpanId == envelope.SpanId), "ack stage span");
                    Check.Equal(envelope.SpanId, dispatch.ParentSpanId, "dispatch stage under envelope");
                    Check.True(envelopeTrace.Any(a => a.DisplayName == "handler:MessageReceived" && a.ParentSpanId == dispatch.SpanId), "handler span under dispatch");
                    Check.False(envelopeTrace.Any(a => a.TagObjects.Any(t => Convert.ToString(t.Value) == "hello")), "message text never on a span");

                    capture.Require(EasySlackTelemetryNames.SocketConnects, T("easyslack.reconnect", "False", "easyslack.outcome", "success"));
                    capture.Require(EasySlackTelemetryNames.SocketConnectDuration, T("easyslack.reconnect", "False", "easyslack.outcome", "success"));
                    Check.True(capture.Find(EasySlackTelemetryNames.SocketConnectionsActive).Any(m => m.Value == 1), "active connection incremented");
                    Check.True(capture.Find(EasySlackTelemetryNames.SocketConnectionsActive).Any(m => m.Value == -1), "active connection decremented on stop");
                    capture.Require(EasySlackTelemetryNames.StateTransitions, T("easyslack.connection.state", "connecting"));
                    capture.Require(EasySlackTelemetryNames.StateTransitions, T("easyslack.connection.state", "connected"));
                    capture.Require(EasySlackTelemetryNames.StateTransitions, T("easyslack.connection.state", "stopping"));
                    capture.Require(EasySlackTelemetryNames.StateTransitions, T("easyslack.connection.state", "disconnected"));
                    capture.Require(EasySlackTelemetryNames.Envelopes, T("easyslack.envelope.type", "events_api", "easyslack.outcome", "success"));
                    capture.Require(EasySlackTelemetryNames.EnvelopeDuration, T("easyslack.envelope.type", "events_api", "easyslack.outcome", "success"));
                    foreach (string stage in new string[] { "parse", "ack", "dispatch" })
                    {
                        capture.Require(EasySlackTelemetryNames.EnvelopeStageDuration, T("easyslack.stage", stage, "easyslack.outcome", "success"));
                        capture.Require(EasySlackTelemetryNames.EnvelopeStageEvents, T("easyslack.stage", stage, "easyslack.outcome", "success"));
                    }

                    capture.Require(EasySlackTelemetryNames.EventsReceived, T("slack.event.type", "message"));
                    capture.Require(EasySlackTelemetryNames.Messages, T("easyslack.message.disposition", "dispatched"));
                    capture.Require(EasySlackTelemetryNames.HandlerInvocations, T("easyslack.event", "MessageReceived", "easyslack.outcome", "success"));
                    capture.Require(EasySlackTelemetryNames.HandlerDuration, T("easyslack.event", "MessageReceived", "easyslack.outcome", "success"));
                    Check.True(capture.Require(EasySlackTelemetryNames.SocketMessageSize).Any(m => m.Value > 0 && m.Unit == "By"), "message size recorded in bytes");
                    capture.Require(EasySlackTelemetryNames.Operations, T("easyslack.operation", "start", "easyslack.outcome", "success"));
                    capture.Require(EasySlackTelemetryNames.Operations, T("easyslack.operation", "stop", "easyslack.outcome", "success"));
                    capture.Require(EasySlackTelemetryNames.ReceiveLoopExits, T("easyslack.loop.exit_reason", "canceled"));

                    capture.RecordObservables();
                    double nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                    Check.True(capture.Require(EasySlackTelemetryNames.EnvelopeLastSuccessTime).Any(m => m.Value > nowSeconds - 60 && m.Value <= nowSeconds + 1), "last success timestamp is recent");
                }),

                Case("SocketConnectFailureRecorded", "A refused Socket Mode open records a failed connect and a failed start", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":false,\"error\":\"telemetry_socket_refused\"}");

                    await Check.ThrowsAsync<InvalidOperationException>(() => h.Connector.StartAsync(ct), "start fails").ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.ApiRequests, T("slack.api.method", "apps.connections.open", "easyslack.outcome", "slack_error", "error.type", "telemetry_socket_refused"));
                    capture.Require(EasySlackTelemetryNames.SocketConnects, T("easyslack.reconnect", "False", "easyslack.outcome", "error", "error.type", "System.InvalidOperationException"));
                    capture.Require(EasySlackTelemetryNames.Errors, T("easyslack.component", "socket", "error.type", "System.InvalidOperationException"));
                    capture.Require(EasySlackTelemetryNames.Operations, T("easyslack.operation", "start", "easyslack.outcome", "error", "error.type", "System.InvalidOperationException"));

                    Activity connect = capture.SpansNamed("slack socket_mode.connect").Last(a => a.Status == ActivityStatusCode.Error);
                    Check.True(connect.Events.Any(e => e.Name == "exception"), "exception event on connect span");
                }),

                Case("MalformedEnvelopeRecorded", "A malformed payload records a parse stage failure and an invalid envelope", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();

                    await Check.ThrowsAsync<JsonException>(() => h.Connector.ProcessSocketMessageAsync("{not json", ct), "malformed").ConfigureAwait(false);

                    CapturedMeasurement envelope = capture.Require(EasySlackTelemetryNames.Envelopes, T("easyslack.envelope.type", "invalid", "easyslack.outcome", "error"))[0];
                    Check.Contains(Convert.ToString(envelope.Tags["error.type"]), "Json", "json error type");
                    capture.Require(EasySlackTelemetryNames.EnvelopeStageEvents, T("easyslack.stage", "parse", "easyslack.outcome", "error"));
                    Check.True(capture.Find(EasySlackTelemetryNames.Errors, T("easyslack.component", "envelope")).Any(), "envelope error counted");
                }),

                Case("HandlerFailureRecorded", "A throwing handler records handler, dispatch stage, and envelope failures and still propagates", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Connector.MessageReceived += (sender, args) => throw new NotSupportedException("handler boom");

                    await Check.ThrowsAsync<NotSupportedException>(() => h.Connector.ProcessSocketMessageAsync("{\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"channel\":\"C1\",\"ts\":\"1.1\"}}}", ct), "handler throws").ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.HandlerInvocations, T("easyslack.event", "MessageReceived", "easyslack.outcome", "error", "error.type", "System.NotSupportedException"));
                    capture.Require(EasySlackTelemetryNames.Errors, T("easyslack.component", "handler", "error.type", "System.NotSupportedException"));
                    capture.Require(EasySlackTelemetryNames.EnvelopeStageEvents, T("easyslack.stage", "dispatch", "easyslack.outcome", "error", "error.type", "System.NotSupportedException"));
                    capture.Require(EasySlackTelemetryNames.Envelopes, T("easyslack.envelope.type", "events_api", "easyslack.outcome", "error", "error.type", "System.NotSupportedException"));
                    Activity handler = capture.RequireSpan("handler:MessageReceived", "error.type", "System.NotSupportedException");
                    Check.Equal(ActivityStatusCode.Error, handler.Status, "handler span error status");
                }),

                Case("ServerDisconnectRecorded", "A disconnect envelope records a server_request disconnect span nested under dispatch", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    string envelopeId = "disc-" + Guid.NewGuid().ToString("N");

                    await h.Connector.ProcessSocketMessageAsync("{\"envelope_id\":\"" + envelopeId + "\",\"type\":\"disconnect\",\"reason\":\"warning\"}", ct).ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.SocketDisconnects, T("easyslack.disconnect.source", "server_request", "easyslack.will_reconnect", "False"));
                    capture.Require(EasySlackTelemetryNames.Envelopes, T("easyslack.envelope.type", "disconnect", "easyslack.outcome", "success"));
                    Activity envelope = capture.RequireSpan("slack socket_mode.envelope", "slack.envelope.id", envelopeId);
                    Activity disconnect = capture.SpansInTrace(envelope.TraceId).Single(a => a.DisplayName == "easyslack disconnect");
                    Check.Equal("warning", (string?)disconnect.GetTagItem(EasySlackTelemetryNames.AttributeDisconnectReason), "reason on span");
                }),

                Case("TransportDisconnectRecorded", "Losing the socket without auto-reconnect records a transport disconnect and a no_reconnect loop exit", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson(SocketOpenResponse);

                    int disconnects = 0;
                    h.Connector.Disconnected += (sender, args) =>
                    {
                        Interlocked.Increment(ref disconnects);
                        return Task.CompletedTask;
                    };

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    await capture.WaitForAsync(() => capture.Find(EasySlackTelemetryNames.ReceiveLoopExits, T("easyslack.loop.exit_reason", "no_reconnect")).Any(), ct).ConfigureAwait(false);
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);

                    Check.True(disconnects >= 1, "disconnect raised");
                    capture.Require(EasySlackTelemetryNames.SocketDisconnects, T("easyslack.disconnect.source", "transport", "easyslack.will_reconnect", "False"));
                    Activity disconnect = capture.RequireSpan("easyslack disconnect", "easyslack.disconnect.source", "transport");
                    Check.Equal("System.Net.WebSockets.WebSocketException", (string?)disconnect.GetTagItem("error.type"), "transport error type on span");
                    Check.True(disconnect.Links.Any(), "disconnect span links to the connection");
                    Check.True(capture.Find(EasySlackTelemetryNames.SocketConnectionsActive).Any(m => m.Value == -1), "active connection decremented on disconnect");
                }),

                Case("ReconnectRecorded", "A dropped socket with auto-reconnect records the backoff and a successful reconnect", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create(autoReconnect: true);
                    h.Options.InitialReconnectDelayMs = 250;
                    h.Socket.CloseThenKeepOpen(1);
                    h.Http.EnqueueJson(SocketOpenResponse);
                    h.Http.EnqueueJson(SocketOpenResponse);

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    await capture.WaitForAsync(() => capture.Find(EasySlackTelemetryNames.SocketConnects, T("easyslack.reconnect", "True", "easyslack.outcome", "success")).Any(), ct).ConfigureAwait(false);
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.SocketDisconnects, T("easyslack.disconnect.source", "transport", "easyslack.will_reconnect", "True"));
                    Check.True(capture.Require(EasySlackTelemetryNames.SocketReconnectDelay).Any(m => Math.Abs(m.Value - 0.25) < 0.0001), "backoff delay recorded in seconds");
                    Activity disconnect = capture.RequireSpan("easyslack disconnect", "easyslack.will_reconnect", "True");
                    Check.True(capture.SpansInTrace(disconnect.TraceId).Any(a => a.DisplayName == "slack socket_mode.connect" && a.ParentSpanId == disconnect.SpanId), "reconnect traced under disconnect span");
                }),

                Case("FailedReconnectRecorded", "A failed reconnect attempt records a reconnect error, backs off further, and then records the successful retry", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create(autoReconnect: true);
                    h.Options.InitialReconnectDelayMs = 250;
                    h.Socket.CloseThenKeepOpen(1);
                    h.Http.EnqueueJson(SocketOpenResponse);
                    h.Http.EnqueueJson("{\"ok\":false,\"error\":\"internal_error\"}");
                    h.Http.EnqueueJson(SocketOpenResponse);

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    await capture.WaitForAsync(() => capture.Find(EasySlackTelemetryNames.SocketConnects, T("easyslack.reconnect", "True", "easyslack.outcome", "success")).Any(), ct).ConfigureAwait(false);
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.SocketConnects, T("easyslack.reconnect", "True", "easyslack.outcome", "error", "error.type", "System.InvalidOperationException"));
                    Check.True(capture.Require(EasySlackTelemetryNames.SocketReconnectDelay).Any(m => Math.Abs(m.Value - 0.5) < 0.0001), "second attempt backed off to double the delay");
                    Check.False(capture.Find(EasySlackTelemetryNames.ReceiveLoopExits, T("easyslack.loop.exit_reason", "faulted")).Any(), "receive loop never faulted");
                }),

                Case("ActionRequiredAndEventTypesRecorded", "Unsupported envelopes and app_rate_limited events record action codes and event types", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();

                    await h.Connector.ProcessSocketMessageAsync("{\"type\":\"interactive\"}", ct).ConfigureAwait(false);
                    await h.Connector.ProcessSocketMessageAsync("{\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"app_rate_limited\"}}}", ct).ConfigureAwait(false);
                    await h.Connector.ProcessSocketMessageAsync("{\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"reaction_added\"}}}", ct).ConfigureAwait(false);
                    await h.Connector.ProcessSocketMessageAsync("{\"type\":\"events_api\"}", ct).ConfigureAwait(false);
                    await h.Connector.ProcessSocketMessageAsync("{\"type\":\"hello\"}", ct).ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.ActionRequired, T("easyslack.action.code", "unsupported_socket_envelope"));
                    capture.Require(EasySlackTelemetryNames.ActionRequired, T("easyslack.action.code", "app_rate_limited"));
                    capture.Require(EasySlackTelemetryNames.Envelopes, T("easyslack.envelope.type", "other", "easyslack.outcome", "success"));
                    capture.Require(EasySlackTelemetryNames.Envelopes, T("easyslack.envelope.type", "hello", "easyslack.outcome", "success"));
                    capture.Require(EasySlackTelemetryNames.EventsReceived, T("slack.event.type", "app_rate_limited"));
                    capture.Require(EasySlackTelemetryNames.EventsReceived, T("slack.event.type", "other"));
                    capture.Require(EasySlackTelemetryNames.EventsReceived, T("slack.event.type", "missing"));
                    capture.Require(EasySlackTelemetryNames.EnvelopeStageEvents, T("easyslack.stage", "ack", "easyslack.outcome", "skipped"));
                    Check.False(capture.Find(EasySlackTelemetryNames.Envelopes).Any(m => Convert.ToString(m.Tags["easyslack.envelope.type"]) == "interactive"), "raw envelope type never used as a label");
                }),

                Case("SubtypeMessageDispositionRecorded", "A message with a subtype records the skipped_subtype disposition", async ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    using ConnectorHarness h = ConnectorHarness.Create();

                    await h.Connector.ProcessSocketMessageAsync("{\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"subtype\":\"bot_message\",\"channel\":\"C1\",\"ts\":\"1.1\"}}}", ct).ConfigureAwait(false);

                    capture.Require(EasySlackTelemetryNames.Messages, T("easyslack.message.disposition", "skipped_subtype"));
                }),

                Case("BuildInfoReported", "The build info gauge reports 1 with the library version", ct =>
                {
                    using TelemetryCapture capture = new TelemetryCapture();
                    capture.RecordObservables();

                    CapturedMeasurement info = capture.Require(EasySlackTelemetryNames.BuildInfo)[0];
                    Check.Equal(1d, info.Value, "build info value");
                    string? version = Convert.ToString(info.Tags["easyslack.version"]);
                    Check.True(!string.IsNullOrWhiteSpace(version) && version != "unknown", "version label populated");
                    Check.DoesNotContain(version, "+", "no source revision suffix");
                    return Task.CompletedTask;
                }),

                Case("LabelNormalizationIsBounded", "Slack error codes and API paths normalize to bounded label values", ct =>
                {
                    Check.Equal("channel_not_found", EasySlackTelemetry.NormalizeCode("channel_not_found"), "valid code kept");
                    Check.Equal("unknown_error", EasySlackTelemetry.NormalizeCode(null), "null code");
                    Check.Equal("unknown_error", EasySlackTelemetry.NormalizeCode("  "), "blank code");
                    Check.Equal("other", EasySlackTelemetry.NormalizeCode("Has Spaces And Caps"), "free text collapses");
                    Check.Equal("other", EasySlackTelemetry.NormalizeCode(new string('a', 65)), "overlong collapses");
                    Check.Equal("users.info", EasySlackTelemetry.ApiMethodFromPath("users.info?user=U1"), "query stripped");
                    Check.Equal("auth.test", EasySlackTelemetry.ApiMethodFromPath("auth.test"), "plain path");
                    Check.Equal("other", EasySlackTelemetry.NormalizeEnvelopeType("interactive"), "unknown envelope type");
                    Check.Equal("events_api", EasySlackTelemetry.NormalizeEnvelopeType("EVENTS_API"), "case-insensitive envelope type");
                    Check.Equal("other", EasySlackTelemetry.NormalizeEventType(null), "null event type");
                    return Task.CompletedTask;
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Telemetry", cases);
        }

        private static Dictionary<string, string> T(params string[] keyValues)
        {
            Dictionary<string, string> tags = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < keyValues.Length; i += 2)
            {
                tags[keyValues[i]] = keyValues[i + 1];
            }

            return tags;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, body);
        }
    }
}
