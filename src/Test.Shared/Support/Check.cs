namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Raised when a test assertion fails. Kept independent of any test runner so descriptors stay runner-agnostic.
    /// </summary>
    public class TestAssertionException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestAssertionException"/> class.
        /// </summary>
        /// <param name="message">The failure message.</param>
        public TestAssertionException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Provides lightweight, runner-agnostic assertions used by Touchstone test descriptors.
    /// Every failure throws a <see cref="TestAssertionException"/> which Touchstone captures as a failed test.
    /// </summary>
    public static class Check
    {
        /// <summary>
        /// Asserts that a condition is true.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="message">The failure message.</param>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new TestAssertionException("Expected true: " + message);
        }

        /// <summary>
        /// Asserts that a condition is false.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="message">The failure message.</param>
        public static void False(bool condition, string message)
        {
            if (condition) throw new TestAssertionException("Expected false: " + message);
        }

        /// <summary>
        /// Asserts that two values are equal.
        /// </summary>
        /// <typeparam name="TValue">The compared value type.</typeparam>
        /// <param name="expected">The expected value.</param>
        /// <param name="actual">The actual value.</param>
        /// <param name="label">The assertion label.</param>
        public static void Equal<TValue>(TValue expected, TValue actual, string label)
        {
            if (!EqualityComparer<TValue>.Default.Equals(expected, actual))
            {
                throw new TestAssertionException(label + ": expected <" + FormatValue(expected) + "> but got <" + FormatValue(actual) + ">");
            }
        }

        /// <summary>
        /// Asserts that a reference is null.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="label">The assertion label.</param>
        public static void Null(object? value, string label)
        {
            if (value != null) throw new TestAssertionException(label + ": expected null but got <" + value + ">");
        }

        /// <summary>
        /// Asserts that a reference is not null.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="label">The assertion label.</param>
        public static void NotNull(object? value, string label)
        {
            if (value == null) throw new TestAssertionException(label + ": expected non-null value");
        }

        /// <summary>
        /// Asserts that a string contains the specified substring (ordinal comparison).
        /// </summary>
        /// <param name="value">The string to inspect.</param>
        /// <param name="substring">The expected substring.</param>
        /// <param name="label">The assertion label.</param>
        public static void Contains(string? value, string substring, string label)
        {
            if (value == null || !value.Contains(substring, StringComparison.Ordinal))
            {
                throw new TestAssertionException(label + ": expected <" + value + "> to contain <" + substring + ">");
            }
        }

        /// <summary>
        /// Asserts that a string does not contain the specified substring (ordinal comparison).
        /// </summary>
        /// <param name="value">The string to inspect.</param>
        /// <param name="substring">The substring that must be absent.</param>
        /// <param name="label">The assertion label.</param>
        public static void DoesNotContain(string? value, string substring, string label)
        {
            if (value != null && value.Contains(substring, StringComparison.Ordinal))
            {
                throw new TestAssertionException(label + ": expected <" + value + "> to not contain <" + substring + ">");
            }
        }

        /// <summary>
        /// Asserts that the supplied synchronous action throws the specified exception type.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The action expected to throw.</param>
        /// <param name="label">The assertion label.</param>
        /// <returns>The thrown exception.</returns>
        public static TException Throws<TException>(Action action, string label) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException expected)
            {
                return expected;
            }
            catch (Exception unexpected)
            {
                throw new TestAssertionException(label + ": expected " + typeof(TException).Name + " but got " + unexpected.GetType().Name + " - " + unexpected.Message);
            }

            throw new TestAssertionException(label + ": expected " + typeof(TException).Name + " to be thrown but nothing was thrown");
        }

        /// <summary>
        /// Asserts that the supplied asynchronous action throws the specified exception type.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The action expected to throw.</param>
        /// <param name="label">The assertion label.</param>
        /// <returns>The thrown exception.</returns>
        public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string label) where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException expected)
            {
                return expected;
            }
            catch (Exception unexpected)
            {
                throw new TestAssertionException(label + ": expected " + typeof(TException).Name + " but got " + unexpected.GetType().Name + " - " + unexpected.Message);
            }

            throw new TestAssertionException(label + ": expected " + typeof(TException).Name + " to be thrown but nothing was thrown");
        }

        private static string FormatValue<TValue>(TValue value)
        {
            return value == null ? "null" : value.ToString() ?? "null";
        }
    }
}
