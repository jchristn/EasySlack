namespace Test.Shared.Support
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
        private int _ForcedCloses = 0;

        /// <summary>
        /// Gets the sent text frames.
        /// </summary>
        public List<string> SentMessages { get; } = new List<string>();

        /// <summary>
        /// Gets a value indicating whether the socket was closed.
        /// </summary>
        public bool CloseCalled { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether the socket should remain open when the receive
        /// queue drains instead of reporting a close frame. When true, a drained receive blocks until
        /// the cancellation token fires, keeping the connector in the connected state for deterministic
        /// lifecycle assertions.
        /// </summary>
        public bool KeepOpenWhenDrained { get; set; }

        /// <summary>
        /// Simulates the socket dropping a fixed number of times before it settles into an open,
        /// blocking state. Each forced close surfaces to the connector as a <see cref="WebSocketMessageType.Close"/>
        /// frame, exercising the exception-driven reconnect path deterministically. After the forced
        /// closes are exhausted, a drained receive honours <see cref="KeepOpenWhenDrained"/>.
        /// </summary>
        /// <param name="count">The number of close frames to emit on drain before keeping the socket open.</param>
        public void CloseThenKeepOpen(int count)
        {
            _ForcedCloses = count;
            KeepOpenWhenDrained = true;
        }

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
                if (_ForcedCloses > 0)
                {
                    _ForcedCloses--;
                    _State = WebSocketState.CloseReceived;
                    WebSocketReceiveResult forcedClose = new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "closed");
                    return Task.FromResult(forcedClose);
                }

                if (KeepOpenWhenDrained)
                {
                    TaskCompletionSource<WebSocketReceiveResult> pending = new TaskCompletionSource<WebSocketReceiveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                    cancellationToken.Register(() => pending.TrySetException(new OperationCanceledException(cancellationToken)));
                    return pending.Task;
                }

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
            CloseCalled = true;
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
