namespace EasySlack
{
    /// <summary>
    /// Represents the result of posting a Slack message.
    /// </summary>
    public class SlackSendMessageResult
    {
        /// <summary>
        /// Gets or sets a value indicating whether the send succeeded.
        /// </summary>
        public bool Ok { get; set; }

        /// <summary>
        /// Gets or sets the Slack error code when available.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Gets or sets the destination conversation identifier.
        /// </summary>
        public string? ChannelId { get; set; }

        /// <summary>
        /// Gets or sets the Slack timestamp for the created message.
        /// </summary>
        public string? Timestamp { get; set; }
    }
}
