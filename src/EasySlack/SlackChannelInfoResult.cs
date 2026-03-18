namespace EasySlack
{
    /// <summary>
    /// Represents the result of a Slack conversation info request.
    /// </summary>
    public class SlackChannelInfoResult
    {
        /// <summary>
        /// Gets or sets a value indicating whether the request succeeded.
        /// </summary>
        public bool Ok { get; set; }

        /// <summary>
        /// Gets or sets the Slack error code when available.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Gets or sets the conversation identifier.
        /// </summary>
        public string? ChannelId { get; set; }

        /// <summary>
        /// Gets or sets the conversation name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the conversation is a channel.
        /// </summary>
        public bool IsChannel { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the conversation is private.
        /// </summary>
        public bool IsPrivate { get; set; }
    }
}
