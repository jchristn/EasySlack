namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One measurement observed by a <see cref="TelemetryCapture"/>.
    /// </summary>
    internal sealed class CapturedMeasurement
    {
        /// <summary>
        /// Gets the instrument name.
        /// </summary>
        public string Instrument { get; }

        /// <summary>
        /// Gets the instrument unit, or null.
        /// </summary>
        public string? Unit { get; }

        /// <summary>
        /// Gets the measured value converted to a double.
        /// </summary>
        public double Value { get; }

        /// <summary>
        /// Gets the measurement tags, keyed by attribute name.
        /// </summary>
        public IReadOnlyDictionary<string, object?> Tags { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CapturedMeasurement"/> class.
        /// </summary>
        /// <param name="instrument">The instrument name.</param>
        /// <param name="unit">The instrument unit.</param>
        /// <param name="value">The measured value.</param>
        /// <param name="tags">The measurement tags.</param>
        public CapturedMeasurement(string instrument, string? unit, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
            Unit = unit;
            Value = value;

            Dictionary<string, object?> copy = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            Tags = copy;
        }

        /// <summary>
        /// Returns true when every expected tag is present with a matching value. Values are compared by their
        /// invariant string form, so booleans are written "True" or "False".
        /// </summary>
        /// <param name="expected">The expected tags.</param>
        /// <returns>True when all expected tags match.</returns>
        public bool Matches(IReadOnlyDictionary<string, string> expected)
        {
            foreach (KeyValuePair<string, string> pair in expected)
            {
                if (!Tags.TryGetValue(pair.Key, out object? actual)) return false;
                string? actualText = Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture);
                if (!string.Equals(actualText, pair.Value, StringComparison.Ordinal)) return false;
            }

            return true;
        }
    }
}
