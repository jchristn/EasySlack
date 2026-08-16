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
    /// Exercises connector lifecycle: connection state, start/stop, disposal, and logging.
    /// </summary>
    public static class LifecycleSuite
    {
        private const string SuiteId = "Lifecycle";
        private const string SocketOpenResponse = "{\"ok\":true,\"url\":\"wss://example.test/socket\"}";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("InitialStateDisconnected", "A new connector reports Disconnected", ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    Check.Equal(SlackConnectionState.Disconnected, h.Connector.ConnectionState, "initial state");
                    return Task.CompletedTask;
                }),

                Case("StartConnectsAndFiresConnected", "StartAsync connects and fires Connected", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Socket.KeepOpenWhenDrained = true;
                    h.Http.EnqueueJson(SocketOpenResponse);

                    List<SlackConnectedEventArgs> connected = new List<SlackConnectedEventArgs>();
                    h.Connector.Connected += (sender, args) =>
                    {
                        connected.Add(args);
                        return Task.CompletedTask;
                    };

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    Check.Equal(SlackConnectionState.Connected, h.Connector.ConnectionState, "state after start");
                    Check.Equal(1, connected.Count, "connected event count");
                    Check.False(connected[0].IsReconnect, "first connect is not a reconnect");
                    Check.Equal("wss://example.test/socket", connected[0].SocketUri, "socket uri");

                    await h.Connector.StopAsync(ct).ConfigureAwait(false);
                    Check.Equal(SlackConnectionState.Disconnected, h.Connector.ConnectionState, "state after stop");
                    Check.True(h.Socket.CloseCalled, "socket closed");
                }),

                Case("StartWhileConnectedThrows", "StartAsync throws when already connected", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Socket.KeepOpenWhenDrained = true;
                    h.Http.EnqueueJson(SocketOpenResponse);

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    await Check.ThrowsAsync<InvalidOperationException>(() => h.Connector.StartAsync(ct), "double start").ConfigureAwait(false);
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);
                }),

                Case("StartSocketOpenFailureThrows", "StartAsync throws and stays Disconnected when Slack refuses the socket", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":false,\"error\":\"invalid_auth\"}");

                    await Check.ThrowsAsync<InvalidOperationException>(() => h.Connector.StartAsync(ct), "socket open failure").ConfigureAwait(false);
                    Check.Equal(SlackConnectionState.Disconnected, h.Connector.ConnectionState, "state after failure");
                }),

                Case("StartSocketOpenMissingUrlThrows", "StartAsync throws when Slack accepts the socket open but returns no URL", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":true}");

                    await Check.ThrowsAsync<InvalidOperationException>(() => h.Connector.StartAsync(ct), "missing socket url").ConfigureAwait(false);
                    Check.Equal(SlackConnectionState.Disconnected, h.Connector.ConnectionState, "state after missing url");
                }),

                Case("StopWhenDisconnectedIsNoOp", "StopAsync is a no-op when never started", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);
                    Check.Equal(SlackConnectionState.Disconnected, h.Connector.ConnectionState, "state remains disconnected");
                }),

                Case("StartUsesAppTokenForSocket", "StartAsync opens the socket with the app token", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Socket.KeepOpenWhenDrained = true;
                    h.Http.Enqueue(request =>
                    {
                        Check.Equal("https://slack.com/api/apps.connections.open", request.RequestUri!.ToString(), "socket open url");
                        Check.NotNull(request.Headers.Authorization, "auth header");
                        Check.Equal("xapp-test", request.Headers.Authorization!.Parameter, "app token bearer");
                        return StubHttpMessageHandler.CreateJsonResponse(SocketOpenResponse);
                    });

                    await h.Connector.StartAsync(ct).ConfigureAwait(false);
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);
                }),

                Case("DisposeThenStartThrows", "StartAsync throws ObjectDisposedException after disposal", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Connector.Dispose();
                    await Check.ThrowsAsync<ObjectDisposedException>(() => h.Connector.StartAsync(ct), "start after dispose").ConfigureAwait(false);
                }),

                Case("StopAfterDisposeIsNoOp", "StopAsync is a no-op after disposal", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Connector.Dispose();
                    await h.Connector.StopAsync(ct).ConfigureAwait(false);
                }),

                Case("DisposeAsyncIsIdempotent", "DisposeAsync can be called repeatedly", async ct =>
                {
                    ConnectorHarness h = ConnectorHarness.Create();
                    await h.Connector.DisposeAsync().ConfigureAwait(false);
                    await h.Connector.DisposeAsync().ConfigureAwait(false);
                    h.Dispose();
                }),

                Case("LoggerReceivesDiagnostics", "The logger callback receives prefixed diagnostics", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    List<string> messages = new List<string>();
                    h.Connector.Logger = message => messages.Add(message);
                    h.Http.EnqueueJson("{\"ok\":true,\"team\":\"T\"}");

                    await h.Connector.ValidateConnectionAsync(ct).ConfigureAwait(false);
                    Check.True(messages.Count > 0, "logger received messages");
                    Check.Contains(messages[0], "[EasySlack]", "log prefix");
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Connector Lifecycle", cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, body);
        }
    }
}
