namespace Test.Automated.Support
{
    using System;
    using EasySlack.Internal;

    /// <summary>
    /// Returns a pre-created fake WebSocket.
    /// </summary>
    internal class FakeManagedWebSocketFactory : IManagedWebSocketFactory
    {
        private readonly IManagedWebSocket _WebSocket;

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeManagedWebSocketFactory"/> class.
        /// </summary>
        /// <param name="webSocket">The fake socket to return.</param>
        public FakeManagedWebSocketFactory(IManagedWebSocket webSocket)
        {
            _WebSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
        }

        /// <summary>
        /// Returns the configured fake WebSocket.
        /// </summary>
        /// <returns>The fake WebSocket.</returns>
        public IManagedWebSocket Create()
        {
            return _WebSocket;
        }
    }
}
