namespace Test.Shared.Suites
{
    using EasySlack;
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Exercises the Slack Web API surface of <see cref="SlackConnector"/> using stubbed HTTP responses.
    /// </summary>
    public static class WebApiSuite
    {
        private const string SuiteId = "WebApi";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("ValidateParsesResponse", "ValidateConnection parses auth.test response", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(request =>
                    {
                        Check.Equal(HttpMethod.Get, request.Method, "auth.test method");
                        Check.Equal("https://slack.com/api/auth.test", request.RequestUri!.ToString(), "auth.test url");
                        Check.NotNull(request.Headers.Authorization, "auth header");
                        Check.Equal("Bearer", request.Headers.Authorization!.Scheme, "auth scheme");
                        Check.Equal("xoxb-test", request.Headers.Authorization!.Parameter, "bot token bearer");
                        return StubHttpMessageHandler.CreateJsonResponse("{\"ok\":true,\"team\":\"EasySlack\",\"team_id\":\"T1\",\"user\":\"bot\",\"user_id\":\"U1\",\"bot_id\":\"B1\"}");
                    });

                    SlackValidationResult result = await h.Connector.ValidateConnectionAsync(ct).ConfigureAwait(false);
                    Check.True(result.Ok, "ok");
                    Check.Equal("EasySlack", result.TeamName, "team name");
                    Check.Equal("T1", result.TeamId, "team id");
                    Check.Equal("bot", result.UserName, "user name");
                    Check.Equal("U1", result.UserId, "user id");
                    Check.Equal("B1", result.BotId, "bot id");
                }),

                Case("ValidateSurfacesError", "ValidateConnection surfaces a Slack error", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":false,\"error\":\"invalid_auth\"}");

                    SlackValidationResult result = await h.Connector.ValidateConnectionAsync(ct).ConfigureAwait(false);
                    Check.False(result.Ok, "ok false");
                    Check.Equal("invalid_auth", result.Error, "error");
                }),

                Case("ValidateHttpErrorThrows", "ValidateConnection throws on non-success status", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(_ => StubHttpMessageHandler.CreateJsonResponse("{}", HttpStatusCode.InternalServerError));

                    await Check.ThrowsAsync<HttpRequestException>(() => h.Connector.ValidateConnectionAsync(ct), "http error").ConfigureAwait(false);
                }),

                Case("SendToUserOpensThenPosts", "SendMessageToUser opens a conversation then posts", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(request =>
                    {
                        Check.Equal(HttpMethod.Post, request.Method, "conversations.open method");
                        Check.Equal("https://slack.com/api/conversations.open", request.RequestUri!.ToString(), "conversations.open url");
                        string body = ReadBody(request);
                        Check.Contains(body, "\"users\":\"U123\"", "users payload");
                        return StubHttpMessageHandler.CreateJsonResponse("{\"ok\":true,\"channel\":{\"id\":\"D123\"}}");
                    });
                    h.Http.Enqueue(request =>
                    {
                        Check.Equal("https://slack.com/api/chat.postMessage", request.RequestUri!.ToString(), "chat.postMessage url");
                        string body = ReadBody(request);
                        Check.Contains(body, "\"channel\":\"D123\"", "channel payload");
                        Check.Contains(body, "\"text\":\"hello\"", "text payload");
                        Check.DoesNotContain(body, "thread_ts", "no thread ts");
                        return StubHttpMessageHandler.CreateJsonResponse("{\"ok\":true,\"channel\":\"D123\",\"ts\":\"123.456\"}");
                    });

                    SlackSendMessageResult result = await h.Connector.SendMessageToUserAsync("U123", "hello", ct).ConfigureAwait(false);
                    Check.True(result.Ok, "ok");
                    Check.Equal("D123", result.ChannelId, "channel id");
                    Check.Equal("123.456", result.Timestamp, "timestamp");
                }),

                Case("SendToUserOpenFailsThrows", "SendMessageToUser throws when conversations.open fails", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":false,\"error\":\"user_not_found\"}");
                    await Check.ThrowsAsync<InvalidOperationException>(() => h.Connector.SendMessageToUserAsync("U123", "hello", ct), "open fails").ConfigureAwait(false);
                }),

                Case("SendToUserOpenMissingChannelThrows", "SendMessageToUser throws when open returns no channel", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":true}");
                    await Check.ThrowsAsync<InvalidOperationException>(() => h.Connector.SendMessageToUserAsync("U123", "hello", ct), "missing channel").ConfigureAwait(false);
                }),

                Case("SendToUserOpenEmptyChannelThrows", "SendMessageToUser throws when open returns an empty channel id", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":true,\"channel\":{\"id\":\"\"}}");
                    await Check.ThrowsAsync<InvalidOperationException>(() => h.Connector.SendMessageToUserAsync("U123", "hello", ct), "empty channel").ConfigureAwait(false);
                }),

                Case("SendToUserNullIdThrows", "SendMessageToUser rejects a null user id", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    await Check.ThrowsAsync<ArgumentNullException>(() => h.Connector.SendMessageToUserAsync(null!, "hello", ct), "null user id").ConfigureAwait(false);
                }),

                Case("SendToUserWhitespaceIdThrows", "SendMessageToUser rejects a whitespace user id", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    await Check.ThrowsAsync<ArgumentNullException>(() => h.Connector.SendMessageToUserAsync("   ", "hello", ct), "whitespace user id").ConfigureAwait(false);
                }),

                Case("SendToChannelOmitsThreadTs", "SendMessageToChannel omits thread_ts when blank", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(request =>
                    {
                        Check.Equal(HttpMethod.Post, request.Method, "method");
                        string body = ReadBody(request);
                        Check.Contains(body, "\"channel\":\"C123\"", "channel payload");
                        Check.Contains(body, "\"text\":\"hello\"", "text payload");
                        Check.DoesNotContain(body, "thread_ts", "no thread ts");
                        return StubHttpMessageHandler.CreateJsonResponse("{\"ok\":true,\"channel\":\"C123\",\"ts\":\"1.1\"}");
                    });

                    SlackSendMessageResult result = await h.Connector.SendMessageToChannelAsync("C123", "hello", "   ", ct).ConfigureAwait(false);
                    Check.True(result.Ok, "ok");
                    Check.Equal("C123", result.ChannelId, "channel id");
                }),

                Case("SendToChannelIncludesThreadTs", "SendMessageToChannel includes and trims thread_ts", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(request =>
                    {
                        string body = ReadBody(request);
                        Check.Contains(body, "\"thread_ts\":\"123.456\"", "thread ts payload");
                        return StubHttpMessageHandler.CreateJsonResponse("{\"ok\":true,\"channel\":\"C123\",\"ts\":\"789.012\"}");
                    });

                    SlackSendMessageResult result = await h.Connector.SendMessageToChannelAsync("C123", "hello", "  123.456  ", ct).ConfigureAwait(false);
                    Check.Equal("789.012", result.Timestamp, "timestamp");
                }),

                Case("SendToChannelTrimsInputs", "SendMessageToChannel trims channel and text", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(request =>
                    {
                        string body = ReadBody(request);
                        Check.Contains(body, "\"channel\":\"C123\"", "trimmed channel");
                        Check.Contains(body, "\"text\":\"hi\"", "trimmed text");
                        return StubHttpMessageHandler.CreateJsonResponse("{\"ok\":true,\"channel\":\"C123\",\"ts\":\"1.1\"}");
                    });

                    await h.Connector.SendMessageToChannelAsync("  C123  ", "  hi  ", null, ct).ConfigureAwait(false);
                }),

                Case("SendToChannelSurfacesError", "SendMessageToChannel surfaces a Slack error", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":false,\"error\":\"channel_not_found\"}");
                    SlackSendMessageResult result = await h.Connector.SendMessageToChannelAsync("C123", "hello", null, ct).ConfigureAwait(false);
                    Check.False(result.Ok, "ok false");
                    Check.Equal("channel_not_found", result.Error, "error");
                }),

                Case("SendToChannelNullChannelThrows", "SendMessageToChannel rejects a null channel id", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    await Check.ThrowsAsync<ArgumentNullException>(() => h.Connector.SendMessageToChannelAsync(null!, "hello", null, ct), "null channel").ConfigureAwait(false);
                }),

                Case("SendToChannelNullTextThrows", "SendMessageToChannel rejects a null text", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    await Check.ThrowsAsync<ArgumentNullException>(() => h.Connector.SendMessageToChannelAsync("C123", null!, null, ct), "null text").ConfigureAwait(false);
                }),

                Case("GetChannelInfoParses", "GetChannelInfo parses a conversation payload", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(request =>
                    {
                        Check.Equal(HttpMethod.Get, request.Method, "method");
                        Check.Equal("https://slack.com/api/conversations.info?channel=C123", request.RequestUri!.ToString(), "url");
                        return StubHttpMessageHandler.CreateJsonResponse("{\"ok\":true,\"channel\":{\"id\":\"C123\",\"name\":\"general\",\"is_channel\":true,\"is_private\":false}}");
                    });

                    SlackChannelInfoResult result = await h.Connector.GetChannelInfoAsync("C123", ct).ConfigureAwait(false);
                    Check.True(result.Ok, "ok");
                    Check.Equal("C123", result.ChannelId, "channel id");
                    Check.Equal("general", result.Name, "name");
                    Check.True(result.IsChannel, "is channel");
                    Check.False(result.IsPrivate, "is private");
                }),

                Case("GetChannelInfoEscapesId", "GetChannelInfo URL-escapes the channel id", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(request =>
                    {
                        Check.Contains(request.RequestUri!.AbsoluteUri, "channel=C%201", "escaped id");
                        return StubHttpMessageHandler.CreateJsonResponse("{\"ok\":true,\"channel\":{\"id\":\"C 1\"}}");
                    });

                    await h.Connector.GetChannelInfoAsync("C 1", ct).ConfigureAwait(false);
                }),

                Case("GetChannelInfoNullThrows", "GetChannelInfo rejects a null channel id", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    await Check.ThrowsAsync<ArgumentNullException>(() => h.Connector.GetChannelInfoAsync(null!, ct), "null channel").ConfigureAwait(false);
                }),

                Case("GetChannelInfoSurfacesError", "GetChannelInfo surfaces a Slack error", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":false,\"error\":\"channel_not_found\"}");
                    SlackChannelInfoResult result = await h.Connector.GetChannelInfoAsync("C123", ct).ConfigureAwait(false);
                    Check.False(result.Ok, "ok false");
                    Check.Equal("channel_not_found", result.Error, "error");
                    Check.Null(result.ChannelId, "channel id null");
                }),

                Case("GetUserInfoParses", "GetUserInfo parses a user payload", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.Enqueue(request =>
                    {
                        Check.Equal(HttpMethod.Get, request.Method, "method");
                        Check.Equal("https://slack.com/api/users.info?user=U1", request.RequestUri!.ToString(), "url");
                        return StubHttpMessageHandler.CreateJsonResponse("{\"ok\":true,\"user\":{\"id\":\"U1\",\"name\":\"bot\",\"real_name\":\"Bot Real\",\"is_bot\":true,\"deleted\":false,\"profile\":{\"display_name\":\"BotDisplay\"}}}");
                    });

                    SlackUserInfoResult result = await h.Connector.GetUserInfoAsync("U1", ct).ConfigureAwait(false);
                    Check.True(result.Ok, "ok");
                    Check.Equal("U1", result.UserId, "user id");
                    Check.Equal("bot", result.UserName, "user name");
                    Check.Equal("Bot Real", result.RealName, "real name");
                    Check.Equal("BotDisplay", result.DisplayName, "display name");
                    Check.True(result.IsBot, "is bot");
                    Check.False(result.IsDeleted, "is deleted");
                }),

                Case("GetUserInfoNullThrows", "GetUserInfo rejects a null user id", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    await Check.ThrowsAsync<ArgumentNullException>(() => h.Connector.GetUserInfoAsync(null!, ct), "null user").ConfigureAwait(false);
                }),

                Case("GetUserInfoSurfacesError", "GetUserInfo surfaces a Slack error", async ct =>
                {
                    using ConnectorHarness h = ConnectorHarness.Create();
                    h.Http.EnqueueJson("{\"ok\":false,\"error\":\"user_not_found\"}");
                    SlackUserInfoResult result = await h.Connector.GetUserInfoAsync("U1", ct).ConfigureAwait(false);
                    Check.False(result.Ok, "ok false");
                    Check.Equal("user_not_found", result.Error, "error");
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Slack Web API", cases);
        }

        private static string ReadBody(HttpRequestMessage request)
        {
            return request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<System.Threading.CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, body);
        }
    }
}
