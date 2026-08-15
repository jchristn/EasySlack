namespace Test.Shared.Suites
{
    using EasySlack;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Exercises Socket Mode message handling end to end through a started connector.
    /// </summary>
    public static class SocketProcessingSuite
    {
        private const string SuiteId = "SocketProcessing";
        private const string SocketOpenResponse = "{\"ok\":true,\"url\":\"wss://example.test/socket\"}";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("TopLevelMessageDispatchedAndAcked", "A top-level message is dispatched with no thread timestamp and acked", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson(SocketOpenResponse);

                    List<SlackMessageReceivedEventArgs> events = new List<SlackMessageReceivedEventArgs>();
                    h.Connector.MessageReceived += (sender, args) =>
                    {
                        events.Add(args);
                        return Task.CompletedTask;
                    };

                    h.Socket.EnqueueIncomingText("{\"envelope_id\":\"abc\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"channel\":\"C1\",\"user\":\"U1\",\"text\":\"hello\",\"ts\":\"123.456\"}}}");

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    await WaitForAsync(() => events.Count >= 1 && h.Socket.SentMessages.Count >= 1, ct).ConfigureAwait(false);
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);

                    Check.Equal(1, events.Count, "message event count");
                    Check.Equal("hello", events[0].Text, "text");
                    Check.Equal("123.456", events[0].Timestamp, "timestamp");
                    Check.Null(events[0].ThreadTimestamp, "thread timestamp");
                    Check.Contains(h.Socket.SentMessages[0], "\"envelope_id\":\"abc\"", "ack payload");
                }),

                Case("ThreadedMessagePopulatesThreadTs", "A threaded message populates the thread timestamp and acks", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson(SocketOpenResponse);

                    List<SlackMessageReceivedEventArgs> events = new List<SlackMessageReceivedEventArgs>();
                    h.Connector.MessageReceived += (sender, args) =>
                    {
                        events.Add(args);
                        return Task.CompletedTask;
                    };

                    h.Socket.EnqueueIncomingText("{\"envelope_id\":\"thread-1\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"channel\":\"C1\",\"user\":\"U1\",\"text\":\"reply\",\"ts\":\"456.789\",\"thread_ts\":\"123.456\"}}}");

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    await WaitForAsync(() => events.Count >= 1 && h.Socket.SentMessages.Count >= 1, ct).ConfigureAwait(false);
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);

                    Check.Equal(1, events.Count, "message event count");
                    Check.Equal("123.456", events[0].ThreadTimestamp, "thread timestamp");
                    Check.Contains(h.Socket.SentMessages[0], "\"envelope_id\":\"thread-1\"", "ack payload");
                }),

                Case("SubtypeMessageAckedNotDispatched", "A message with a subtype is acked but not dispatched", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson(SocketOpenResponse);

                    int count = 0;
                    h.Connector.MessageReceived += (sender, args) =>
                    {
                        Interlocked.Increment(ref count);
                        return Task.CompletedTask;
                    };

                    h.Socket.EnqueueIncomingText("{\"envelope_id\":\"sub-1\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"channel\":\"C1\",\"user\":\"U1\",\"text\":\"ignored\",\"ts\":\"123.456\",\"subtype\":\"bot_message\"}}}");

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    await WaitForAsync(() => h.Socket.SentMessages.Count >= 1, ct).ConfigureAwait(false);
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);

                    Check.Equal(0, count, "subtype messages are not dispatched");
                    Check.Contains(h.Socket.SentMessages[0], "\"envelope_id\":\"sub-1\"", "ack payload");
                }),

                Case("UnsupportedEnvelopeFiresActionRequired", "An unsupported envelope fires ActionRequired", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();

                    List<SlackActionRequiredEventArgs> events = new List<SlackActionRequiredEventArgs>();
                    h.Connector.ActionRequired += (sender, args) =>
                    {
                        events.Add(args);
                        return Task.CompletedTask;
                    };

                    await h.Connector.ProcessSocketMessageAsync("{\"type\":\"interactive\"}", ct).ConfigureAwait(false);

                    Check.Equal(1, events.Count, "action required count");
                    Check.Equal("unsupported_socket_envelope", events[0].Code, "action code");
                }),

                Case("DisconnectEnvelopeFiresDisconnected", "A disconnect envelope fires Disconnected with the reason", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();

                    List<SlackDisconnectedEventArgs> events = new List<SlackDisconnectedEventArgs>();
                    h.Connector.Disconnected += (sender, args) =>
                    {
                        events.Add(args);
                        return Task.CompletedTask;
                    };

                    await h.Connector.ProcessSocketMessageAsync("{\"type\":\"disconnect\",\"reason\":\"warning\"}", ct).ConfigureAwait(false);

                    Check.Equal(1, events.Count, "disconnect event count");
                    Check.Equal("warning", events[0].Reason, "disconnect reason");
                    Check.False(events[0].WillReconnect, "will reconnect false when auto reconnect disabled");
                }),

                Case("SocketCloseFiresDisconnected", "Losing the socket fires Disconnected without reconnecting", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson(SocketOpenResponse);

                    List<SlackDisconnectedEventArgs> events = new List<SlackDisconnectedEventArgs>();
                    h.Connector.Disconnected += (sender, args) =>
                    {
                        events.Add(args);
                        return Task.CompletedTask;
                    };

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    await WaitForAsync(() => events.Count >= 1, ct).ConfigureAwait(false);
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);

                    Check.True(events.Count >= 1, "disconnect fired on socket close");
                    Check.False(events[0].WillReconnect, "will not reconnect");
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Socket Mode Processing", cases);
        }

        private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            for (int i = 0; i < 200; i++)
            {
                if (condition()) return;
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            throw new TestAssertionException("Timed out waiting for the expected connector activity.");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, body);
        }
    }
}
