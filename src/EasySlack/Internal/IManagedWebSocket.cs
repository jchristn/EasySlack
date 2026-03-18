namespace EasySlack.Internal
{
    using System;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Provides an abstraction over a client WebSocket for testability.
    /// </summary>
    internal interface IManagedWebSocket : IDisposable
    {
        /// <summary>
        /// Gets the current WebSocket state.
        /// </summary>
        WebSocketState State { get; }

        /// <summary>
        /// Connects to the supplied URI.
        /// </summary>
        /// <param name="uri">The target URI.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when connected.</returns>
        Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

        /// <summary>
        /// Receives data from the WebSocket.
        /// </summary>
        /// <param name="buffer">The receive buffer.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The receive result.</returns>
        Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);

        /// <summary>
        /// Sends a text message.
        /// </summary>
        /// <param name="text">The text to send.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the send finishes.</returns>
        Task SendTextAsync(string text, CancellationToken cancellationToken);

        /// <summary>
        /// Closes the WebSocket.
        /// </summary>
        /// <param name="closeStatus">The close status.</param>
        /// <param name="statusDescription">The close description.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the close finishes.</returns>
        Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken);
    }
}
