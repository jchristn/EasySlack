namespace Test.Automated.Support
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Provides deterministic HTTP responses for connector tests.
    /// </summary>
    internal class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _Handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
        private bool _Disposed = false;

        /// <summary>
        /// Gets the requests observed by the handler.
        /// </summary>
        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        /// <summary>
        /// Enqueues a response factory.
        /// </summary>
        /// <param name="handler">The response factory.</param>
        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _Handlers.Enqueue(handler ?? throw new ArgumentNullException(nameof(handler)));
        }

        /// <summary>
        /// Handles a send request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
            Requests.Add(request);

            if (_Handlers.Count < 1)
            {
                HttpResponseMessage defaultResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                defaultResponse.Content = new StringContent("{\"ok\":false,\"error\":\"no_stub_response\"}");
                return Task.FromResult(defaultResponse);
            }

            Func<HttpRequestMessage, HttpResponseMessage> handler = _Handlers.Dequeue();
            HttpResponseMessage response = handler(request);
            return Task.FromResult(response);
        }

        /// <summary>
        /// Releases the tracked request resources.
        /// </summary>
        /// <param name="disposing">True when disposing managed resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (_Disposed)
            {
                base.Dispose(disposing);
                return;
            }

            if (disposing)
            {
                foreach (HttpRequestMessage request in Requests)
                {
                    request.Dispose();
                }

                Requests.Clear();
                _Handlers.Clear();
            }

            _Disposed = true;
            base.Dispose(disposing);
        }
    }
}
