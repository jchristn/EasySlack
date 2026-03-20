namespace EasySlack
{
    using System;

    /// <summary>
    /// Provides data for an inbound Slack message event.
    /// </summary>
    public class SlackMessageReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the Slack conversation identifier.
        /// </summary>
        public string? ChannelId { get; set; }

        /// <summary>
        /// Gets or sets the Slack user identifier.
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// Gets or sets the message text.
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// Gets or sets the Slack timestamp for the specific inbound message.
        /// </summary>
        public string? Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the Slack thread root timestamp when the message belongs to a thread.
        /// This is usually <see langword="null"/> for top-level messages.
        /// </summary>
        public string? ThreadTimestamp { get; set; }

        /// <summary>
        /// Gets or sets the Slack event subtype.
        /// </summary>
        public string? Subtype { get; set; }

        /// <summary>
        /// Gets or sets the raw JSON payload.
        /// </summary>
        public string? RawPayload { get; set; }
    }
}
