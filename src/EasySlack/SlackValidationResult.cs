namespace EasySlack
{
    /// <summary>
    /// Represents the result of a Slack auth validation request.
    /// </summary>
    public class SlackValidationResult
    {
        /// <summary>
        /// Gets or sets a value indicating whether validation succeeded.
        /// </summary>
        public bool Ok { get; set; }

        /// <summary>
        /// Gets or sets the Slack error code when available.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Gets or sets the Slack team identifier.
        /// </summary>
        public string? TeamId { get; set; }

        /// <summary>
        /// Gets or sets the Slack team name.
        /// </summary>
        public string? TeamName { get; set; }

        /// <summary>
        /// Gets or sets the authenticated user identifier.
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// Gets or sets the authenticated user name.
        /// </summary>
        public string? UserName { get; set; }

        /// <summary>
        /// Gets or sets the authenticated bot identifier.
        /// </summary>
        public string? BotId { get; set; }
    }
}
