namespace EasySlack.Internal
{
    /// <summary>
    /// Creates <see cref="ManagedClientWebSocket"/> instances.
    /// </summary>
    internal class ManagedClientWebSocketFactory : IManagedWebSocketFactory
    {
        /// <summary>
        /// Creates a new managed WebSocket.
        /// </summary>
        /// <returns>The managed WebSocket.</returns>
        public IManagedWebSocket Create()
        {
            return new ManagedClientWebSocket();
        }
    }
}
