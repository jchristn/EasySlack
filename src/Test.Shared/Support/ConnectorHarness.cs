namespace Test.Shared.Support
{
    using EasySlack;
    using EasySlack.Internal;
    using System;
    using System.Net.Http;
    using System.Threading;

    /// <summary>
    /// Builds fully wired <see cref="SlackConnector"/> instances backed by test doubles.
    /// Encapsulates access to the connector's internal test constructor so individual
    /// descriptors stay concise.
    /// </summary>
    internal sealed class ConnectorHarness : IDisposable
    {
        /// <summary>
        /// Gets the stub HTTP handler feeding the connector's Web API calls.
        /// </summary>
        public StubHttpMessageHandler Http { get; }

        /// <summary>
        /// Gets the fake Socket Mode WebSocket.
        /// </summary>
        public FakeManagedWebSocket Socket { get; }

        /// <summary>
        /// Gets the connector options.
        /// </summary>
        public SlackConnectorOptions Options { get; }

        /// <summary>
        /// Gets the connector under test.
        /// </summary>
        public SlackConnector Connector { get; }

        private readonly HttpClient _HttpClient;
        private readonly CancellationTokenSource _Cts;

        private ConnectorHarness(SlackConnectorOptions options)
        {
            Options = options;
            Http = new StubHttpMessageHandler();
            Socket = new FakeManagedWebSocket();

            _HttpClient = new HttpClient(Http);
            _HttpClient.BaseAddress = new Uri(options.ApiBaseUrl, UriKind.Absolute);
            _Cts = new CancellationTokenSource();

            Connector = new SlackConnector(
                options,
                _Cts,
                _HttpClient,
                true,
                new FakeManagedWebSocketFactory(Socket),
                new SocketModeEnvelopeProcessor());
        }

        /// <summary>
        /// Creates a harness with default valid auth material.
        /// </summary>
        /// <param name="autoReconnect">Whether the connector should auto-reconnect.</param>
        /// <returns>The harness.</returns>
        public static ConnectorHarness Create(bool autoReconnect = false)
        {
            SlackConnectorOptions options = new SlackConnectorOptions(new SlackAuthMaterial("xoxb-test", "xapp-test"))
            {
                AutoReconnect = autoReconnect
            };

            return new ConnectorHarness(options);
        }

        /// <summary>
        /// Disposes the connector and its owned resources.
        /// </summary>
        public void Dispose()
        {
            Connector.Dispose();
            _Cts.Dispose();
        }
    }
}
