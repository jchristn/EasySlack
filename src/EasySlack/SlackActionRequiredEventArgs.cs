namespace EasySlack
{
    using System;

    /// <summary>
    /// Provides details about a condition that may require operator attention.
    /// </summary>
    public class SlackActionRequiredEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the machine-friendly action code.
        /// </summary>
        public string? Code { get; set; }

        /// <summary>
        /// Gets or sets the human-readable description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the raw payload associated with the condition.
        /// </summary>
        public string? RawPayload { get; set; }
    }
}
