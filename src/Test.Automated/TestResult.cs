namespace Test.Automated
{
    using System;

    /// <summary>
    /// Stores the outcome of a single test.
    /// </summary>
    public class TestResult
    {
        /// <summary>
        /// Gets or sets the test display name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the test passed.
        /// </summary>
        public bool Passed { get; set; }

        /// <summary>
        /// Gets or sets the runtime in milliseconds.
        /// </summary>
        public long ElapsedMs { get; set; }

        /// <summary>
        /// Gets or sets the summary message for the test.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Gets or sets the exception captured by the test.
        /// </summary>
        public Exception? Exception { get; set; }
    }
}
