namespace EasySlack
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Defines the public API surface for a Slack connector.
    /// </summary>
    public interface ISlackConnector : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Gets or sets the diagnostic logger callback.
        /// </summary>
        Action<string>? Logger { get; set; }

        /// <summary>
        /// Fired when a Slack message event is received.
        /// </summary>
        event AsyncEventHandler<SlackMessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// Fired when the connector establishes or re-establishes a Socket Mode connection.
        /// </summary>
        event AsyncEventHandler<SlackConnectedEventArgs>? Connected;

        /// <summary>
        /// Fired when the connector loses its Socket Mode connection.
        /// </summary>
        event AsyncEventHandler<SlackDisconnectedEventArgs>? Disconnected;

        /// <summary>
        /// Fired when Slack reports a condition that likely requires user intervention.
        /// </summary>
        event AsyncEventHandler<SlackActionRequiredEventArgs>? ActionRequired;

        /// <summary>
        /// Gets the current connection state.
        /// </summary>
        SlackConnectionState ConnectionState { get; }

        /// <summary>
        /// Starts the Socket Mode connection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the initial connection is established.</returns>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the Socket Mode connection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the connector is stopped.</returns>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates the configured Slack bot token.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The validation result.</returns>
        Task<SlackValidationResult> ValidateConnectionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a message to a Slack user by opening or reusing a direct conversation.
        /// </summary>
        /// <param name="userId">The Slack user identifier.</param>
        /// <param name="text">The message text.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The send result.</returns>
        Task<SlackSendMessageResult> SendMessageToUserAsync(string userId, string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a message to a Slack channel or conversation.
        /// </summary>
        /// <param name="channelId">The Slack conversation identifier.</param>
        /// <param name="text">The message text.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The send result.</returns>
        Task<SlackSendMessageResult> SendMessageToChannelAsync(string channelId, string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves Slack conversation metadata.
        /// </summary>
        /// <param name="channelId">The Slack conversation identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The conversation info result.</returns>
        Task<SlackChannelInfoResult> GetChannelInfoAsync(string channelId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves Slack user metadata.
        /// </summary>
        /// <param name="userId">The Slack user identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The user info result.</returns>
        Task<SlackUserInfoResult> GetUserInfoAsync(string userId, CancellationToken cancellationToken = default);
    }
}
