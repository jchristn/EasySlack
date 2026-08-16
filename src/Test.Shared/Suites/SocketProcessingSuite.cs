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

                Case("SocketCloseReconnectsWhenAutoReconnectEnabled", "A dropped socket reconnects and fires Connected with IsReconnect when auto-reconnect is enabled", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create(autoReconnect: true);
                    h.Options.InitialReconnectDelayMs = 250;
                    h.Socket.CloseThenKeepOpen(1);

                    // One socket open for the initial connect, one for the reconnect.
                    h.Http.EnqueueJson(SocketOpenResponse);
                    h.Http.EnqueueJson(SocketOpenResponse);

                    List<SlackConnectedEventArgs> connected = new List<SlackConnectedEventArgs>();
                    List<SlackDisconnectedEventArgs> disconnected = new List<SlackDisconnectedEventArgs>();
                    h.Connector.Connected += (sender, args) =>
                    {
                        connected.Add(args);
                        return Task.CompletedTask;
                    };
                    h.Connector.Disconnected += (sender, args) =>
                    {
                        disconnected.Add(args);
                        return Task.CompletedTask;
                    };

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    await WaitForAsync(() => connected.Count >= 2, ct).ConfigureAwait(false);
                    Check.Equal(SlackConnectionState.Connected, h.Connector.ConnectionState, "connected after reconnect");
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);

                    Check.Equal(2, connected.Count, "connect count (initial + reconnect)");
                    Check.False(connected[0].IsReconnect, "first connect is not a reconnect");
                    Check.True(connected[1].IsReconnect, "second connect is a reconnect");
                    Check.True(disconnected.Count >= 1, "disconnect fired before reconnect");
                    Check.True(disconnected[0].WillReconnect, "will reconnect true");
                    Check.Equal(SlackConnectionState.Disconnected, h.Connector.ConnectionState, "disconnected after stop");
                }),

                Case("DisconnectEnvelopeReconnectsWhenAutoReconnectEnabled", "A disconnect envelope reconnects when auto-reconnect is enabled", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create(autoReconnect: true);
                    h.Options.InitialReconnectDelayMs = 250;
                    h.Socket.KeepOpenWhenDrained = true;

                    h.Http.EnqueueJson(SocketOpenResponse);
                    h.Http.EnqueueJson(SocketOpenResponse);

                    List<SlackConnectedEventArgs> connected = new List<SlackConnectedEventArgs>();
                    List<SlackDisconnectedEventArgs> disconnected = new List<SlackDisconnectedEventArgs>();
                    h.Connector.Connected += (sender, args) =>
                    {
                        connected.Add(args);
                        return Task.CompletedTask;
                    };
                    h.Connector.Disconnected += (sender, args) =>
                    {
                        disconnected.Add(args);
                        return Task.CompletedTask;
                    };

                    h.Socket.EnqueueIncomingText("{\"type\":\"disconnect\",\"reason\":\"refresh_requested\"}");

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    await WaitForAsync(() => connected.Count >= 2, ct).ConfigureAwait(false);
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);

                    Check.Equal(2, connected.Count, "connect count (initial + reconnect)");
                    Check.True(connected[1].IsReconnect, "second connect is a reconnect");
                    Check.True(disconnected.Count >= 1, "disconnect fired");
                    Check.Equal("refresh_requested", disconnected[0].Reason, "disconnect reason preserved");
                    Check.True(disconnected[0].WillReconnect, "will reconnect true");
                }),

                Case("AppRateLimitedFiresActionRequired", "An app_rate_limited event surfaces ActionRequired through the connector", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();

                    List<SlackActionRequiredEventArgs> events = new List<SlackActionRequiredEventArgs>();
                    h.Connector.ActionRequired += (sender, args) =>
                    {
                        events.Add(args);
                        return Task.CompletedTask;
                    };

                    await h.Connector.ProcessSocketMessageAsync("{\"envelope_id\":\"rl-1\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"app_rate_limited\"}}}", ct).ConfigureAwait(false);

                    Check.Equal(1, events.Count, "action required count");
                    Check.Equal("app_rate_limited", events[0].Code, "action code");
                }),

                Case("MultipleMessageHandlersAllInvoked", "Every subscribed MessageReceived handler is invoked", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();

                    int first = 0;
                    int second = 0;
                    h.Connector.MessageReceived += (sender, args) =>
                    {
                        Interlocked.Increment(ref first);
                        return Task.CompletedTask;
                    };
                    h.Connector.MessageReceived += (sender, args) =>
                    {
                        Interlocked.Increment(ref second);
                        return Task.CompletedTask;
                    };

                    await h.Connector.ProcessSocketMessageAsync("{\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"channel\":\"C1\",\"user\":\"U1\",\"text\":\"hi\",\"ts\":\"1.1\"}}}", ct).ConfigureAwait(false);

                    Check.Equal(1, first, "first handler invoked once");
                    Check.Equal(1, second, "second handler invoked once");
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
