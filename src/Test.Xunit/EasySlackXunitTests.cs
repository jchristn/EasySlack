namespace Test.Xunit
{
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.XunitAdapter;
    using global::Xunit;

    /// <summary>
    /// Theory-driven xUnit host that surfaces every EasySlack Touchstone descriptor as an
    /// individual xUnit test case.
    /// </summary>
    public sealed class EasySlackXunitTheoryTests
    {
        /// <summary>
        /// Provides one theory row per non-skipped test case descriptor.
        /// </summary>
        /// <returns>The theory data.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            return new TouchstoneTheoryData(EasySlackTestSuites.All);
        }

        /// <summary>
        /// Executes a single shared descriptor.
        /// </summary>
        /// <param name="testCase">The descriptor to execute.</param>
        /// <returns>A task that completes when the case finishes.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Fact-style xUnit host that runs every EasySlack descriptor in a single test and aggregates
    /// failures. Guarantees the whole suite is exercised even where theory enumeration is limited.
    /// </summary>
    public sealed class EasySlackXunitFactTests : TouchstoneFactBase
    {
        /// <summary>
        /// Gets the suites exercised by this host.
        /// </summary>
        protected override System.Collections.Generic.IReadOnlyList<TestSuiteDescriptor> Suites
        {
            get { return EasySlackTestSuites.All; }
        }

        /// <summary>
        /// Runs all descriptors.
        /// </summary>
        /// <returns>A task that completes when every case has run.</returns>
        [Fact]
        public async Task RunAll()
        {
            await RunAllAsync();
        }
    }
}
