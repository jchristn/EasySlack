namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Exercises the internal Socket Mode envelope parser directly, covering each branch.
    /// </summary>
    public static class EnvelopeProcessorSuite
    {
        private const string SuiteId = "EnvelopeProcessor";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("AcksWhenEnvelopeIdPresent", "An envelope id is acknowledged", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"envelope_id\":\"e1\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"text\":\"x\"}}}").ConfigureAwait(false);
                    Check.Equal(1, capture.Acks.Count, "ack count");
                    Check.Equal("e1", capture.Acks[0], "ack id");
                }),

                Case("HelloIsNotAckedOrDispatched", "A hello envelope is ignored", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"type\":\"hello\"}").ConfigureAwait(false);
                    Check.Equal(0, capture.Acks.Count, "no ack");
                    Check.Equal(0, capture.Messages.Count, "no message");
                    Check.Equal(0, capture.Actions.Count, "no action");
                    Check.Equal(0, capture.Disconnects.Count, "no disconnect");
                }),

                Case("MessageEventCaptured", "A message event is captured with all fields", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"envelope_id\":\"e2\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"channel\":\"C1\",\"user\":\"U1\",\"text\":\"hi\",\"ts\":\"1.1\",\"thread_ts\":\"0.9\"}}}").ConfigureAwait(false);
                    Check.Equal(1, capture.Messages.Count, "message count");
                    Check.Equal("C1", capture.Messages[0].ChannelId, "channel");
                    Check.Equal("U1", capture.Messages[0].UserId, "user");
                    Check.Equal("hi", capture.Messages[0].Text, "text");
                    Check.Equal("1.1", capture.Messages[0].Timestamp, "ts");
                    Check.Equal("0.9", capture.Messages[0].ThreadTimestamp, "thread ts");
                    Check.NotNull(capture.Messages[0].RawPayload, "raw payload");
                }),

                Case("SubtypeMessageStillCaptured", "The parser forwards messages with a subtype (filtering is the connector's job)", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"envelope_id\":\"e3\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"text\":\"x\",\"subtype\":\"bot_message\"}}}").ConfigureAwait(false);
                    Check.Equal(1, capture.Messages.Count, "message count");
                    Check.Equal("bot_message", capture.Messages[0].Subtype, "subtype");
                }),

                Case("AppRateLimitedFiresAction", "An app_rate_limited event fires ActionRequired", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"envelope_id\":\"e4\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"app_rate_limited\"}}}").ConfigureAwait(false);
                    Check.Equal(1, capture.Actions.Count, "action count");
                    Check.Equal("app_rate_limited", capture.Actions[0].Code, "action code");
                }),

                Case("UnsupportedTypeFiresAction", "An unsupported envelope type fires ActionRequired", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"type\":\"slash_commands\"}").ConfigureAwait(false);
                    Check.Equal(1, capture.Actions.Count, "action count");
                    Check.Equal("unsupported_socket_envelope", capture.Actions[0].Code, "action code");
                }),

                Case("DisconnectReasonFromReason", "A disconnect envelope uses the reason field", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"type\":\"disconnect\",\"reason\":\"refresh_requested\"}").ConfigureAwait(false);
                    Check.Equal(1, capture.Disconnects.Count, "disconnect count");
                    Check.Equal("refresh_requested", capture.Disconnects[0].Reason, "reason");
                }),

                Case("DisconnectReasonFromDebugInfo", "A disconnect envelope derives a reason from debug_info", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"type\":\"disconnect\",\"debug_info\":{\"host\":\"applink-1\",\"build_number\":\"42\"}}").ConfigureAwait(false);
                    Check.Equal(1, capture.Disconnects.Count, "disconnect count");
                    Check.Contains(capture.Disconnects[0].Reason, "applink-1", "host in reason");
                    Check.Contains(capture.Disconnects[0].Reason, "42", "build in reason");
                }),

                Case("DisconnectReasonDefault", "A bare disconnect envelope uses a default reason", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"type\":\"disconnect\"}").ConfigureAwait(false);
                    Check.Equal(1, capture.Disconnects.Count, "disconnect count");
                    Check.Equal("Slack requested disconnect.", capture.Disconnects[0].Reason, "default reason");
                }),

                Case("WhitespacePayloadIgnored", "A whitespace payload is ignored", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("   ").ConfigureAwait(false);
                    Check.Equal(0, capture.Acks.Count, "no ack");
                    Check.Equal(0, capture.Messages.Count, "no message");
                    Check.Equal(0, capture.Actions.Count, "no action");
                    Check.Equal(0, capture.Disconnects.Count, "no disconnect");
                }),

                Case("EventsApiWithoutPayloadIgnored", "An events_api envelope without a payload dispatches nothing", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"envelope_id\":\"e5\",\"type\":\"events_api\"}").ConfigureAwait(false);
                    Check.Equal(1, capture.Acks.Count, "still acked");
                    Check.Equal(0, capture.Messages.Count, "no message");
                    Check.Equal(0, capture.Actions.Count, "no action");
                }),

                Case("EventsApiWithoutEventIgnored", "An events_api payload without an event dispatches nothing", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"envelope_id\":\"e6\",\"type\":\"events_api\",\"payload\":{}}").ConfigureAwait(false);
                    Check.Equal(0, capture.Messages.Count, "no message");
                    Check.Equal(0, capture.Actions.Count, "no action");
                }),

                Case("NonMessageEventIgnored", "An events_api event that is neither message nor rate-limited is ignored", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await capture.RunAsync("{\"envelope_id\":\"e7\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"reaction_added\"}}}").ConfigureAwait(false);
                    Check.Equal(0, capture.Messages.Count, "no message");
                    Check.Equal(0, capture.Actions.Count, "no action");
                }),

                Case("InvalidJsonThrows", "A malformed payload raises a JSON exception", async ct =>
                {
                    EnvelopeCapture capture = new EnvelopeCapture();
                    await Check.ThrowsAsync<JsonException>(() => capture.RunAsync("{ not valid json"), "invalid json").ConfigureAwait(false);
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Socket Mode Envelope Parsing", cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<System.Threading.CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, body);
        }
    }
}
