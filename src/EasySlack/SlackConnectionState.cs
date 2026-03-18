namespace EasySlack
{
    /// <summary>
    /// Represents the connector connection state.
    /// </summary>
    public enum SlackConnectionState
    {
        /// <summary>
        /// The connector is not connected.
        /// </summary>
        Disconnected = 0,

        /// <summary>
        /// The connector is currently connecting.
        /// </summary>
        Connecting = 1,

        /// <summary>
        /// The connector is connected.
        /// </summary>
        Connected = 2,

        /// <summary>
        /// The connector is stopping.
        /// </summary>
        Stopping = 3
    }
}
