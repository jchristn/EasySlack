namespace Test.Automated.Suites
{
    using EasySlack;
    using EasySlack.Internal;
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Automated.Support;

    /// <summary>
    /// Verifies Socket Mode envelope handling.
    /// </summary>
    public class SocketModeProcessingTests : TestSuite
    {
        /// <summary>
        /// Gets the suite name.
        /// </summary>
        public override string Name
        {
            get
            {
                return "Socket Mode";
            }
        }

        /// <summary>
        /// Runs the suite tests.
        /// </summary>
        /// <returns>A task that completes when the tests finish.</returns>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Message Envelope Fires Message Event And Ack", TestMessageEnvelopeFiresMessageEventAndAckAsync).ConfigureAwait(false);
            await RunTest("Unsupported Envelope Fires Action Required", TestUnsupportedEnvelopeFiresActionRequiredAsync).ConfigureAwait(false);
            await RunTest("Disconnect Envelope Fires Disconnected Event", TestDisconnectEnvelopeFiresDisconnectedEventAsync).ConfigureAwait(false);
        }

        private async Task TestMessageEnvelopeFiresMessageEventAndAckAsync()
        {
            FakeManagedWebSocket fakeSocket = new FakeManagedWebSocket();
            using (SlackConnector connector = CreateConnector(fakeSocket))
            {
                List<SlackMessageReceivedEventArgs> events = new List<SlackMessageReceivedEventArgs>();
                connector.MessageReceived += async (sender, eventArgs) =>
                {
                    events.Add(eventArgs);
                    await Task.CompletedTask.ConfigureAwait(false);
                };

                fakeSocket.EnqueueIncomingText("{\"envelope_id\":\"abc\",\"type\":\"events_api\",\"payload\":{\"event\":{\"type\":\"message\",\"channel\":\"C1\",\"user\":\"U1\",\"text\":\"hello\",\"ts\":\"123.456\"}}}");
                fakeSocket.EnqueueIncomingText("{\"type\":\"hello\"}");

                await connector.StartAsync().ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);
                await connector.StopAsync().ConfigureAwait(false);

                AssertEqual(1, events.Count, "message event count");
                AssertEqual("hello", events[0].Text, "message text");
                Assert(fakeSocket.SentMessages[0].Contains("\"envelope_id\":\"abc\"", StringComparison.Ordinal), "ack payload");
            }
        }

        private async Task TestUnsupportedEnvelopeFiresActionRequiredAsync()
        {
            FakeManagedWebSocket fakeSocket = new FakeManagedWebSocket();
            using (SlackConnector connector = CreateConnector(fakeSocket))
            {
                List<SlackActionRequiredEventArgs> events = new List<SlackActionRequiredEventArgs>();
                connector.ActionRequired += async (sender, eventArgs) =>
                {
                    events.Add(eventArgs);
                    await Task.CompletedTask.ConfigureAwait(false);
                };

                await connector.ProcessSocketMessageAsync("{\"type\":\"interactive\"}", CancellationToken.None).ConfigureAwait(false);
                AssertEqual(1, events.Count, "action required count");
                AssertEqual("unsupported_socket_envelope", events[0].Code, "action code");
            }
        }

        private async Task TestDisconnectEnvelopeFiresDisconnectedEventAsync()
        {
            FakeManagedWebSocket fakeSocket = new FakeManagedWebSocket();
            using (SlackConnector connector = CreateConnector(fakeSocket))
            {
                List<SlackDisconnectedEventArgs> events = new List<SlackDisconnectedEventArgs>();
                connector.Disconnected += async (sender, eventArgs) =>
                {
                    events.Add(eventArgs);
                    await Task.CompletedTask.ConfigureAwait(false);
                };

                await connector.ProcessSocketMessageAsync("{\"type\":\"disconnect\",\"reason\":\"warning\"}", CancellationToken.None).ConfigureAwait(false);
                AssertEqual(1, events.Count, "disconnect event count");
                AssertEqual("warning", events[0].Reason, "disconnect reason");
            }
        }

        private static SlackConnector CreateConnector(FakeManagedWebSocket fakeSocket)
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler();
            handler.Enqueue(request => CreateJsonResponse("{\"ok\":true,\"url\":\"wss://example.test/socket\"}"));

            SlackConnectorOptions options = new SlackConnectorOptions(new SlackAuthMaterial("xoxb-test", "xapp-test"));
            options.AutoReconnect = false;

            HttpClient httpClient = new HttpClient(handler);
            httpClient.BaseAddress = new Uri(options.ApiBaseUrl, UriKind.Absolute);

            FakeManagedWebSocketFactory webSocketFactory = new FakeManagedWebSocketFactory(fakeSocket);
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            return new SlackConnector(options, cancellationTokenSource, httpClient, true, webSocketFactory, new SocketModeEnvelopeProcessor());
        }

        private static HttpResponseMessage CreateJsonResponse(string json)
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return response;
        }
    }
}
