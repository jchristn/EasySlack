namespace Test.Automated.Support
{
    using EasySlack.Internal;
    using System;
    using System.Collections.Generic;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Provides a controllable WebSocket implementation for connector tests.
    /// </summary>
    internal class FakeManagedWebSocket : IManagedWebSocket
    {
        private readonly Queue<string> _ReceiveQueue = new Queue<string>();
        private WebSocketState _State = WebSocketState.None;

        /// <summary>
        /// Gets the sent text frames.
        /// </summary>
        public List<string> SentMessages { get; } = new List<string>();

        /// <summary>
        /// Gets the current state.
        /// </summary>
        public WebSocketState State
        {
            get
            {
                return _State;
            }
        }

        /// <summary>
        /// Queues an inbound text frame.
        /// </summary>
        /// <param name="message">The message to queue.</param>
        public void EnqueueIncomingText(string message)
        {
            _ReceiveQueue.Enqueue(message ?? throw new ArgumentNullException(nameof(message)));
        }

        /// <summary>
        /// Connects the socket.
        /// </summary>
        /// <param name="uri">The target URI.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A completed task.</returns>
        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            _State = WebSocketState.Open;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Receives data from the queue.
        /// </summary>
        /// <param name="buffer">The destination buffer.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The receive result.</returns>
        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_ReceiveQueue.Count < 1)
            {
                _State = WebSocketState.CloseReceived;
                WebSocketReceiveResult closeResult = new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "closed");
                return Task.FromResult(closeResult);
            }

            string message = _ReceiveQueue.Dequeue();
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            Array.Copy(bytes, 0, buffer.Array!, buffer.Offset, bytes.Length);
            WebSocketReceiveResult result = new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Sends a text frame.
        /// </summary>
        /// <param name="text">The text to send.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A completed task.</returns>
        public Task SendTextAsync(string text, CancellationToken cancellationToken)
        {
            SentMessages.Add(text);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Closes the socket.
        /// </summary>
        /// <param name="closeStatus">The close status.</param>
        /// <param name="statusDescription">The close description.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A completed task.</returns>
        public Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            _State = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Disposes the fake socket.
        /// </summary>
        public void Dispose()
        {
            _State = WebSocketState.Closed;
        }
    }
}
