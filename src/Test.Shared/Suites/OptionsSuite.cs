namespace Test.Shared.Suites
{
    using EasySlack;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Exercises <see cref="SlackConnectorOptions"/> defaults, clamping, and normalization.
    /// </summary>
    public static class OptionsSuite
    {
        private const string SuiteId = "Options";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Sync("Defaults", "Default option values are correct", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    Check.True(options.AutoReconnect, "auto reconnect default");
                    Check.Equal(1000, options.InitialReconnectDelayMs, "initial reconnect default");
                    Check.Equal(30000, options.MaxReconnectDelayMs, "max reconnect default");
                    Check.Equal(16384, options.ReceiveBufferSize, "receive buffer default");
                    Check.Equal("https://slack.com/api/", options.ApiBaseUrl, "api base url default");
                    Check.NotNull(options.Auth, "auth default");
                }),

                Sync("CtorWithAuth", "Constructor stores supplied auth", () =>
                {
                    SlackAuthMaterial auth = new SlackAuthMaterial("xoxb-a", "xapp-a");
                    SlackConnectorOptions options = new SlackConnectorOptions(auth);
                    Check.True(ReferenceEquals(auth, options.Auth), "auth reference");
                }),

                Sync("CtorNullAuthThrows", "Constructor rejects null auth", () =>
                {
                    Check.Throws<ArgumentNullException>(() => new SlackConnectorOptions(null!), "null auth ctor");
                }),

                Sync("AuthSetterNullThrows", "Auth setter rejects null", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    Check.Throws<ArgumentNullException>(() => options.Auth = null!, "null auth setter");
                }),

                Sync("InitialReconnectClampLow", "Initial reconnect delay clamps to minimum", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    options.InitialReconnectDelayMs = 10;
                    Check.Equal(250, options.InitialReconnectDelayMs, "clamp low");
                }),

                Sync("InitialReconnectClampHigh", "Initial reconnect delay clamps to maximum", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    options.InitialReconnectDelayMs = 999999;
                    Check.Equal(60000, options.InitialReconnectDelayMs, "clamp high");
                }),

                Sync("InitialReconnectWithinRange", "Initial reconnect delay within range is preserved", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    options.InitialReconnectDelayMs = 5000;
                    Check.Equal(5000, options.InitialReconnectDelayMs, "within range");
                }),

                Sync("MaxReconnectClampLow", "Max reconnect delay clamps to minimum", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    options.MaxReconnectDelayMs = 10;
                    Check.Equal(1000, options.MaxReconnectDelayMs, "clamp low");
                }),

                Sync("MaxReconnectClampHigh", "Max reconnect delay clamps to maximum", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    options.MaxReconnectDelayMs = 9999999;
                    Check.Equal(300000, options.MaxReconnectDelayMs, "clamp high");
                }),

                Sync("MaxReconnectNotBelowInitial", "Max reconnect delay is raised to at least the initial delay", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    options.InitialReconnectDelayMs = 10000;
                    options.MaxReconnectDelayMs = 2000;
                    Check.Equal(10000, options.MaxReconnectDelayMs, "raised to initial");
                }),

                Sync("ReceiveBufferClampLow", "Receive buffer clamps to minimum", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    options.ReceiveBufferSize = 1;
                    Check.Equal(2048, options.ReceiveBufferSize, "clamp low");
                }),

                Sync("ReceiveBufferClampHigh", "Receive buffer clamps to maximum", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    options.ReceiveBufferSize = 10 * 1024 * 1024;
                    Check.Equal(1024 * 1024, options.ReceiveBufferSize, "clamp high");
                }),

                Sync("ApiBaseUrlAddsTrailingSlash", "Api base URL gains a trailing slash", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    options.ApiBaseUrl = "https://example.com/api";
                    Check.Equal("https://example.com/api/", options.ApiBaseUrl, "trailing slash added");
                }),

                Sync("ApiBaseUrlKeepsTrailingSlash", "Api base URL keeps an existing trailing slash", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    options.ApiBaseUrl = "https://example.com/api/";
                    Check.Equal("https://example.com/api/", options.ApiBaseUrl, "trailing slash preserved");
                }),

                Sync("ApiBaseUrlTrimmed", "Api base URL is trimmed", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    options.ApiBaseUrl = "  https://example.com/api  ";
                    Check.Equal("https://example.com/api/", options.ApiBaseUrl, "trimmed");
                }),

                Sync("ApiBaseUrlNullThrows", "Null api base URL throws", () =>
                {
                    SlackConnectorOptions options = new SlackConnectorOptions();
                    Check.Throws<ArgumentNullException>(() => options.ApiBaseUrl = null!, "null url");
                    Check.Throws<ArgumentNullException>(() => options.ApiBaseUrl = "   ", "whitespace url");
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Slack Connector Options", cases);
        }

        private static TestCaseDescriptor Sync(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, _ =>
            {
                body();
                return Task.CompletedTask;
            });
        }
    }
}
