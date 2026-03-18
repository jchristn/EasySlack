namespace EasySlack.Internal
{
    /// <summary>
    /// Creates managed WebSocket instances.
    /// </summary>
    internal interface IManagedWebSocketFactory
    {
        /// <summary>
        /// Creates a new managed WebSocket.
        /// </summary>
        /// <returns>The managed WebSocket.</returns>
        IManagedWebSocket Create();
    }
}
