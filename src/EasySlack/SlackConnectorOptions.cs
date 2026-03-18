namespace EasySlack
{
    using System;

    /// <summary>
    /// Configures connector behavior.
    /// </summary>
    public class SlackConnectorOptions
    {
        /// <summary>
        /// Gets or sets the Slack authentication material.
        /// </summary>
        public SlackAuthMaterial Auth
        {
            get
            {
                return _Auth;
            }
            set
            {
                _Auth = value ?? throw new ArgumentNullException(nameof(Auth));
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the connector should automatically reconnect.
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// Gets or sets the initial reconnect delay in milliseconds.
        /// </summary>
        public int InitialReconnectDelayMs
        {
            get
            {
                return _InitialReconnectDelayMs;
            }
            set
            {
                _InitialReconnectDelayMs = Clamp(value, 250, 60000);
            }
        }

        /// <summary>
        /// Gets or sets the maximum reconnect delay in milliseconds.
        /// </summary>
        public int MaxReconnectDelayMs
        {
            get
            {
                return _MaxReconnectDelayMs;
            }
            set
            {
                _MaxReconnectDelayMs = Clamp(value, 1000, 300000);
                if (_MaxReconnectDelayMs < _InitialReconnectDelayMs) _MaxReconnectDelayMs = _InitialReconnectDelayMs;
            }
        }

        /// <summary>
        /// Gets or sets the receive buffer size in bytes.
        /// </summary>
        public int ReceiveBufferSize
        {
            get
            {
                return _ReceiveBufferSize;
            }
            set
            {
                _ReceiveBufferSize = Clamp(value, 2048, 1024 * 1024);
            }
        }

        /// <summary>
        /// Gets or sets the Web API base URL.
        /// </summary>
        public string ApiBaseUrl
        {
            get
            {
                return _ApiBaseUrl;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ApiBaseUrl));
                string trimmed = value.Trim();
                if (!trimmed.EndsWith("/", StringComparison.Ordinal)) trimmed += "/";
                _ApiBaseUrl = trimmed;
            }
        }

        private SlackAuthMaterial _Auth = new SlackAuthMaterial("xoxb-placeholder", "xapp-placeholder");
        private int _InitialReconnectDelayMs = 1000;
        private int _MaxReconnectDelayMs = 30000;
        private int _ReceiveBufferSize = 16384;
        private string _ApiBaseUrl = "https://slack.com/api/";

        /// <summary>
        /// Initializes a new instance of the <see cref="SlackConnectorOptions"/> class.
        /// </summary>
        public SlackConnectorOptions()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SlackConnectorOptions"/> class.
        /// </summary>
        /// <param name="auth">The Slack authentication material.</param>
        public SlackConnectorOptions(SlackAuthMaterial auth)
        {
            Auth = auth ?? throw new ArgumentNullException(nameof(auth));
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }
    }
}
