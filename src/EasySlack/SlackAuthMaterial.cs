namespace EasySlack
{
    using System;

    /// <summary>
    /// Stores the Slack tokens required by the connector.
    /// </summary>
    public class SlackAuthMaterial
    {
        /// <summary>
        /// Gets or sets the Slack bot token.
        /// </summary>
        public string BotToken
        {
            get
            {
                return _BotToken;
            }
            set
            {
                _BotToken = SanitizeRequiredToken(value, nameof(BotToken), "xoxb-");
            }
        }

        /// <summary>
        /// Gets or sets the Slack app token used for Socket Mode.
        /// </summary>
        public string AppToken
        {
            get
            {
                return _AppToken;
            }
            set
            {
                _AppToken = SanitizeRequiredToken(value, nameof(AppToken), "xapp-");
            }
        }

        private string _BotToken = string.Empty;
        private string _AppToken = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="SlackAuthMaterial"/> class.
        /// </summary>
        public SlackAuthMaterial()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SlackAuthMaterial"/> class.
        /// </summary>
        /// <param name="botToken">The Slack bot token.</param>
        /// <param name="appToken">The Slack app token.</param>
        public SlackAuthMaterial(string botToken, string appToken)
        {
            BotToken = botToken;
            AppToken = appToken;
        }

        private static string SanitizeRequiredToken(string value, string parameterName, string expectedPrefix)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(parameterName);

            string trimmed = value.Trim();
            if (!trimmed.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Token must begin with " + expectedPrefix, parameterName);
            }

            return trimmed;
        }
    }
}
