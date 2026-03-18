namespace EasySlack
{
    using System;

    /// <summary>
    /// Provides connection established event data.
    /// </summary>
    public class SlackConnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets a value indicating whether the connection is a reconnection.
        /// </summary>
        public bool IsReconnect { get; set; }

        /// <summary>
        /// Gets or sets the socket URI used for the connection.
        /// </summary>
        public string? SocketUri { get; set; }
    }
}
