namespace Test.Automated
{
    using System;
    using System.Threading.Tasks;
    using Test.Automated.Suites;

    /// <summary>
    /// Entry point for the EasySlack automated test runner.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Runs the automated test suites.
        /// </summary>
        /// <param name="args">Command-line arguments passed to the test runner.</param>
        /// <returns><c>0</c> when all tests pass; otherwise <c>1</c>.</returns>
        public static async Task<int> Main(string[] args)
        {
            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
            {
                Console.WriteLine("Usage: Test.Automated [--help]");
                Console.WriteLine();
                Console.WriteLine("Runs the EasySlack automated test suite.");
                return 0;
            }

            TestRunner runner = new TestRunner("EASYSLACK AUTOMATED TEST SUITE");
            runner.AddSuite(new OptionValidationTests());
            runner.AddSuite(new ConnectorApiTests());
            runner.AddSuite(new SocketModeProcessingTests());

            return await runner.RunAllAsync().ConfigureAwait(false);
        }
    }
}
