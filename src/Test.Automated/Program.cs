namespace Test.Automated
{
    using System;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    /// <summary>
    /// Touchstone console runner for the EasySlack test suites. Executes the shared descriptors
    /// exposed by <see cref="EasySlackTestSuites.All"/> and returns a CI-friendly exit code.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Runs the EasySlack Touchstone suites through the console runner.
        /// </summary>
        /// <param name="args">Command-line arguments. Supports <c>--help</c> and <c>--results &lt;path&gt;</c>.</param>
        /// <returns><c>0</c> when all tests pass; otherwise <c>1</c>.</returns>
        public static async Task<int> Main(string[] args)
        {
            string? resultsPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg == "--help" || arg == "-h")
                {
                    PrintUsage();
                    return 0;
                }

                if (arg == "--results" && i + 1 < args.Length)
                {
                    resultsPath = args[++i];
                }
            }

            return await ConsoleRunner.RunAsync(EasySlackTestSuites.All, resultsPath: resultsPath).ConfigureAwait(false);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: Test.Automated [--results <path>] [--help]");
            Console.WriteLine();
            Console.WriteLine("Runs the EasySlack Touchstone test suites via the console runner.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --results <path>   Write structured JSON results to the given file.");
            Console.WriteLine("  --help, -h         Show this help text.");
        }
    }
}
