namespace EasySlack
{
    using System;

    /// <summary>
    /// Provides connection lost event data.
    /// </summary>
    public class SlackDisconnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the disconnect reason.
        /// </summary>
        public string? Reason { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the connector will attempt reconnection.
        /// </summary>
        public bool WillReconnect { get; set; }

        /// <summary>
        /// Gets or sets the exception that caused the disconnect when applicable.
        /// </summary>
        public Exception? Exception { get; set; }
    }
}
