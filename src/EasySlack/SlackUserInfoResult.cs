namespace EasySlack
{
    /// <summary>
    /// Represents the result of a Slack user info request.
    /// </summary>
    public class SlackUserInfoResult
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
        /// Gets or sets the user identifier.
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// Gets or sets the Slack username.
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// Gets or sets the user real name.
        /// </summary>
        public string? RealName { get; set; }

        /// <summary>
        /// Gets or sets the user display name.
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the user is a bot.
        /// </summary>
        public bool IsBot { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the user is deleted.
        /// </summary>
        public bool IsDeleted { get; set; }
    }
}
