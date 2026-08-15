namespace Test.Shared.Support
{
    using EasySlack;
    using EasySlack.Internal;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Captures the callbacks invoked by a <see cref="SocketModeEnvelopeProcessor"/> so envelope
    /// parsing can be asserted in isolation from the connector.
    /// </summary>
    internal sealed class EnvelopeCapture
    {
        /// <summary>
        /// Gets the envelope identifiers that were acknowledged.
        /// </summary>
        public List<string> Acks { get; } = new List<string>();

        /// <summary>
        /// Gets the dispatched message events.
        /// </summary>
        public List<SlackMessageReceivedEventArgs> Messages { get; } = new List<SlackMessageReceivedEventArgs>();

        /// <summary>
        /// Gets the disconnect events.
        /// </summary>
        public List<SlackDisconnectedEventArgs> Disconnects { get; } = new List<SlackDisconnectedEventArgs>();

        /// <summary>
        /// Gets the action-required events.
        /// </summary>
        public List<SlackActionRequiredEventArgs> Actions { get; } = new List<SlackActionRequiredEventArgs>();

        /// <summary>
        /// Runs the supplied JSON payload through a fresh processor, capturing every callback.
        /// </summary>
        /// <param name="json">The raw Socket Mode payload.</param>
        /// <returns>A task that completes when processing finishes.</returns>
        public async Task RunAsync(string json)
        {
            SocketModeEnvelopeProcessor processor = new SocketModeEnvelopeProcessor();

            await processor.ProcessAsync(
                json,
                (envelopeId, ct) =>
                {
                    Acks.Add(envelopeId);
                    return Task.CompletedTask;
                },
                (message, ct) =>
                {
                    Messages.Add(message);
                    return Task.CompletedTask;
                },
                (disconnected, ct) =>
                {
                    Disconnects.Add(disconnected);
                    return Task.CompletedTask;
                },
                (action, ct) =>
                {
                    Actions.Add(action);
                    return Task.CompletedTask;
                },
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
