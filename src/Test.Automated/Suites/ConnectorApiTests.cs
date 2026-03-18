namespace Test.Automated.Suites
{
    using EasySlack;
    using EasySlack.Internal;
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Automated.Support;

    /// <summary>
    /// Verifies Web API behavior for the connector.
    /// </summary>
    public class ConnectorApiTests : TestSuite
    {
        /// <summary>
        /// Gets the suite name.
        /// </summary>
        public override string Name
        {
            get
            {
                return "Connector API";
            }
        }

        /// <summary>
        /// Runs the suite tests.
        /// </summary>
        /// <returns>A task that completes when the tests finish.</returns>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Validate Connection Parses Auth Test Response", TestValidateConnectionParsesAuthResponseAsync).ConfigureAwait(false);
            await RunTest("Send Message To User Opens Conversation Then Posts", TestSendMessageToUserUsesOpenConversationAsync).ConfigureAwait(false);
            await RunTest("Get Channel Info Parses Conversation Payload", TestGetChannelInfoParsesPayloadAsync).ConfigureAwait(false);
        }

        private async Task TestValidateConnectionParsesAuthResponseAsync()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler();
            handler.Enqueue(request =>
            {
                AssertEqual(HttpMethod.Get, request.Method, "auth.test method");
                AssertEqual("https://slack.com/api/auth.test", request.RequestUri!.ToString(), "auth.test path");
                return CreateJsonResponse("{\"ok\":true,\"team\":\"EasySlack\",\"team_id\":\"T1\",\"user\":\"bot\",\"user_id\":\"U1\",\"bot_id\":\"B1\"}");
            });

            using (SlackConnector connector = CreateConnector(handler))
            {
                SlackValidationResult result = await connector.ValidateConnectionAsync().ConfigureAwait(false);
                Assert(result.Ok, "validation should succeed");
                AssertEqual("EasySlack", result.TeamName, "team name");
                AssertEqual("U1", result.UserId, "user id");
            }
        }

        private async Task TestSendMessageToUserUsesOpenConversationAsync()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler();
            handler.Enqueue(request =>
            {
                AssertEqual(HttpMethod.Post, request.Method, "conversations.open method");
                AssertEqual("https://slack.com/api/conversations.open", request.RequestUri!.ToString(), "conversations.open path");
                string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert(body.Contains("\"users\":\"U123\"", StringComparison.Ordinal), "users payload");
                return CreateJsonResponse("{\"ok\":true,\"channel\":{\"id\":\"D123\"}}");
            });
            handler.Enqueue(request =>
            {
                AssertEqual(HttpMethod.Post, request.Method, "chat.postMessage method");
                AssertEqual("https://slack.com/api/chat.postMessage", request.RequestUri!.ToString(), "chat.postMessage path");
                string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert(body.Contains("\"channel\":\"D123\"", StringComparison.Ordinal), "channel payload");
                Assert(body.Contains("\"text\":\"hello\"", StringComparison.Ordinal), "text payload");
                return CreateJsonResponse("{\"ok\":true,\"channel\":\"D123\",\"ts\":\"123.456\"}");
            });

            using (SlackConnector connector = CreateConnector(handler))
            {
                SlackSendMessageResult result = await connector.SendMessageToUserAsync("U123", "hello").ConfigureAwait(false);
                Assert(result.Ok, "message should send");
                AssertEqual("D123", result.ChannelId, "conversation id");
                AssertEqual("123.456", result.Timestamp, "timestamp");
            }
        }

        private async Task TestGetChannelInfoParsesPayloadAsync()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler();
            handler.Enqueue(request =>
            {
                AssertEqual("https://slack.com/api/conversations.info?channel=C123", request.RequestUri!.ToString(), "channel info path");
                return CreateJsonResponse("{\"ok\":true,\"channel\":{\"id\":\"C123\",\"name\":\"general\",\"is_channel\":true,\"is_private\":false}}");
            });

            using (SlackConnector connector = CreateConnector(handler))
            {
                SlackChannelInfoResult result = await connector.GetChannelInfoAsync("C123").ConfigureAwait(false);
                Assert(result.Ok, "channel info should succeed");
                AssertEqual("general", result.Name, "channel name");
                Assert(result.IsChannel, "is channel");
            }
        }

        private static HttpResponseMessage CreateJsonResponse(string json)
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return response;
        }

        private static SlackConnector CreateConnector(StubHttpMessageHandler handler)
        {
            SlackConnectorOptions options = new SlackConnectorOptions(new SlackAuthMaterial("xoxb-test", "xapp-test"));
            HttpClient httpClient = new HttpClient(handler);
            httpClient.BaseAddress = new Uri(options.ApiBaseUrl, UriKind.Absolute);

            FakeManagedWebSocket fakeSocket = new FakeManagedWebSocket();
            FakeManagedWebSocketFactory webSocketFactory = new FakeManagedWebSocketFactory(fakeSocket);
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            return new SlackConnector(options, cancellationTokenSource, httpClient, true, webSocketFactory, new SocketModeEnvelopeProcessor());
        }
    }
}
