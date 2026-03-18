namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Orchestrates test suite execution and prints a summary.
    /// </summary>
    public class TestRunner
    {
        private readonly List<TestSuite> _Suites = new List<TestSuite>();
        private readonly List<TestResult> _AllResults = new List<TestResult>();
        private readonly string _Title;

        /// <summary>
        /// Initializes a new runner.
        /// </summary>
        /// <param name="title">The run title.</param>
        public TestRunner(string title)
        {
            _Title = title ?? throw new ArgumentNullException(nameof(title));
        }

        /// <summary>
        /// Adds a suite to the run.
        /// </summary>
        /// <param name="suite">The suite to add.</param>
        public void AddSuite(TestSuite suite)
        {
            _Suites.Add(suite ?? throw new ArgumentNullException(nameof(suite)));
        }

        /// <summary>
        /// Runs all suites and returns the process exit code.
        /// </summary>
        /// <returns><c>0</c> when all tests pass; otherwise <c>1</c>.</returns>
        public async Task<int> RunAllAsync()
        {
            Console.WriteLine();
            Console.WriteLine("================================================================================");
            Console.WriteLine(_Title);
            Console.WriteLine("================================================================================");

            Stopwatch totalTimer = Stopwatch.StartNew();

            foreach (TestSuite suite in _Suites)
            {
                Console.WriteLine();
                Console.WriteLine("--- " + suite.Name + " ---");
                List<TestResult> results = await suite.RunAsync().ConfigureAwait(false);
                _AllResults.AddRange(results);
            }

            totalTimer.Stop();
            return PrintSummary(totalTimer.ElapsedMilliseconds);
        }

        private int PrintSummary(long totalMs)
        {
            int total = _AllResults.Count;
            int passed = _AllResults.Count(result => result.Passed);
            int failed = total - passed;
            List<TestResult> failedTests = _AllResults.Where(result => !result.Passed).ToList();

            Console.WriteLine();
            Console.WriteLine("================================================================================");
            Console.WriteLine("TEST SUMMARY");
            Console.WriteLine("================================================================================");
            Console.WriteLine("Total: " + total + "  Passed: " + passed + "  Failed: " + failed + "  Runtime: " + totalMs + "ms");

            if (failedTests.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Failed Tests:");
                foreach (TestResult result in failedTests)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("  - " + result.Name);
                    Console.ResetColor();
                    Console.WriteLine(": " + result.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine("================================================================================");
            if (failed == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("RESULT: PASS");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("RESULT: FAIL");
            }

            Console.ResetColor();
            Console.WriteLine("================================================================================");
            Console.WriteLine();

            return failed == 0 ? 0 : 1;
        }
    }
}
