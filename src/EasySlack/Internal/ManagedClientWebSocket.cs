namespace EasySlack.Internal
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Adapts <see cref="ClientWebSocket"/> to <see cref="IManagedWebSocket"/>.
    /// </summary>
    internal class ManagedClientWebSocket : IManagedWebSocket
    {
        private readonly ClientWebSocket _Socket = new ClientWebSocket();
        private bool _Disposed = false;

        /// <summary>
        /// Gets the current WebSocket state.
        /// </summary>
        public WebSocketState State
        {
            get
            {
                return _Socket.State;
            }
        }

        /// <summary>
        /// Connects to the supplied URI.
        /// </summary>
        /// <param name="uri">The target URI.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when connected.</returns>
        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
            await _Socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Receives data from the WebSocket.
        /// </summary>
        /// <param name="buffer">The receive buffer.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The receive result.</returns>
        public async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
            return await _Socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a text message.
        /// </summary>
        /// <param name="text">The text to send.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the send finishes.</returns>
        public async Task SendTextAsync(string text, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            ArraySegment<byte> segment = new ArraySegment<byte>(bytes);
            await _Socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Closes the WebSocket.
        /// </summary>
        /// <param name="closeStatus">The close status.</param>
        /// <param name="statusDescription">The close description.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the close finishes.</returns>
        public async Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);

            if (_Socket.State == WebSocketState.Open || _Socket.State == WebSocketState.CloseReceived)
            {
                await _Socket.CloseAsync(closeStatus, statusDescription, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases the WebSocket resources.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Socket.Dispose();
            _Disposed = true;
        }
    }
}
