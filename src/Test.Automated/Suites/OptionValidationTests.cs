namespace Test.Automated.Suites
{
    using EasySlack;
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Verifies option and auth validation behavior.
    /// </summary>
    public class OptionValidationTests : TestSuite
    {
        /// <summary>
        /// Gets the suite name.
        /// </summary>
        public override string Name
        {
            get
            {
                return "Option Validation";
            }
        }

        /// <summary>
        /// Runs the suite tests.
        /// </summary>
        /// <returns>A task that completes when the tests finish.</returns>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Auth Material Rejects Invalid Prefixes", TestAuthMaterialRejectsInvalidPrefixes).ConfigureAwait(false);
            await RunTest("Reconnect Delays Are Clamped", TestReconnectDelayClamping).ConfigureAwait(false);
            await RunTest("Receive Buffer Is Clamped", TestReceiveBufferClamping).ConfigureAwait(false);
            await RunTest("Api Base Url Normalizes Trailing Slash", TestApiBaseUrlNormalization).ConfigureAwait(false);
        }

        private void TestAuthMaterialRejectsInvalidPrefixes()
        {
            AssertThrows<ArgumentException>(() => new SlackAuthMaterial("bad-token", "xapp-valid"), "bot token prefix");
            AssertThrows<ArgumentException>(() => new SlackAuthMaterial("xoxb-valid", "bad-token"), "app token prefix");
        }

        private void TestReconnectDelayClamping()
        {
            SlackConnectorOptions options = new SlackConnectorOptions(new SlackAuthMaterial("xoxb-valid", "xapp-valid"));
            options.InitialReconnectDelayMs = 10;
            options.MaxReconnectDelayMs = 100;

            AssertEqual(250, options.InitialReconnectDelayMs, "initial reconnect clamp");
            AssertEqual(1000, options.MaxReconnectDelayMs, "max reconnect clamp");
        }

        private void TestReceiveBufferClamping()
        {
            SlackConnectorOptions options = new SlackConnectorOptions(new SlackAuthMaterial("xoxb-valid", "xapp-valid"));
            options.ReceiveBufferSize = 1;
            AssertEqual(2048, options.ReceiveBufferSize, "receive buffer minimum");
        }

        private void TestApiBaseUrlNormalization()
        {
            SlackConnectorOptions options = new SlackConnectorOptions(new SlackAuthMaterial("xoxb-valid", "xapp-valid"));
            options.ApiBaseUrl = "https://example.com/api";
            AssertEqual("https://example.com/api/", options.ApiBaseUrl, "trailing slash");
        }
    }
}
