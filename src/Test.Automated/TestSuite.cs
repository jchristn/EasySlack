namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading.Tasks;

    /// <summary>
    /// Abstract base class for test suites.
    /// </summary>
    public abstract class TestSuite
    {
        /// <summary>
        /// Gets the display name of the suite.
        /// </summary>
        public abstract string Name { get; }

        private List<TestResult> _Results = new List<TestResult>();

        /// <summary>
        /// Runs the suite and returns the collected results.
        /// </summary>
        /// <returns>The collected results.</returns>
        public async Task<List<TestResult>> RunAsync()
        {
            _Results = new List<TestResult>();
            await RunTestsAsync().ConfigureAwait(false);
            return _Results;
        }

        /// <summary>
        /// Implemented by derived classes to define tests.
        /// </summary>
        /// <returns>A task that completes when all suite tests have run.</returns>
        protected abstract Task RunTestsAsync();

        /// <summary>
        /// Runs an asynchronous test body.
        /// </summary>
        /// <param name="name">The test display name.</param>
        /// <param name="action">The test body.</param>
        /// <returns>A task that completes when the result is recorded.</returns>
        protected async Task RunTest(string name, Func<Task> action)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            TestResult result = new TestResult { Name = name };

            try
            {
                await action().ConfigureAwait(false);
                stopwatch.Stop();
                result.Passed = true;
                result.ElapsedMs = stopwatch.ElapsedMilliseconds;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  PASS  ");
                Console.ResetColor();
                Console.WriteLine(name + " (" + result.ElapsedMs + "ms)");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                result.Passed = false;
                result.ElapsedMs = stopwatch.ElapsedMilliseconds;
                result.Exception = exception;
                result.Message = exception.GetType().Name + " - " + exception.Message;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  FAIL  ");
                Console.ResetColor();
                Console.WriteLine(name + " (" + result.ElapsedMs + "ms)");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("         " + result.Message);
                Console.ResetColor();
            }

            _Results.Add(result);
        }

        /// <summary>
        /// Runs a synchronous test body.
        /// </summary>
        /// <param name="name">The test display name.</param>
        /// <param name="action">The test body.</param>
        /// <returns>A task that completes when the result is recorded.</returns>
        protected async Task RunTest(string name, Action action)
        {
            await RunTest(name, () =>
            {
                action();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Asserts that the condition is true.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="message">The failure message.</param>
        protected void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion failed: " + message);
        }

        /// <summary>
        /// Asserts that two values are equal.
        /// </summary>
        /// <typeparam name="TValue">The compared value type.</typeparam>
        /// <param name="expected">The expected value.</param>
        /// <param name="actual">The actual value.</param>
        /// <param name="label">The optional assertion label.</param>
        protected void AssertEqual<TValue>(TValue expected, TValue actual, string? label = null)
        {
            if (!EqualityComparer<TValue>.Default.Equals(expected, actual))
            {
                string message = label == null
                    ? "Expected <" + expected + "> but got <" + actual + ">"
                    : label + ": expected <" + expected + "> but got <" + actual + ">";
                throw new Exception("Assertion failed: " + message);
            }
        }

        /// <summary>
        /// Asserts that the given action throws the specified exception.
        /// </summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The action expected to throw.</param>
        /// <param name="label">The optional assertion label.</param>
        protected void AssertThrows<TException>(Action action, string? label = null) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            string message = label == null
                ? "Expected " + typeof(TException).Name + " to be thrown"
                : label + ": expected " + typeof(TException).Name + " to be thrown";
            throw new Exception("Assertion failed: " + message);
        }
    }
}
