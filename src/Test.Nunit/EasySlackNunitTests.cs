namespace Test.Nunit
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using global::NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// TestCaseSource-driven NUnit host that surfaces every EasySlack Touchstone descriptor as an
    /// individual NUnit test case.
    /// </summary>
    [TestFixture]
    public sealed class EasySlackNunitTestCaseTests
    {
        private static IEnumerable TestCases()
        {
            return new TouchstoneTestCaseSource(EasySlackTestSuites.All);
        }

        /// <summary>
        /// Executes a single shared descriptor.
        /// </summary>
        /// <param name="testCase">The descriptor to execute.</param>
        /// <returns>A task that completes when the case finishes.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Single-test NUnit host that runs every EasySlack descriptor and aggregates failures.
    /// </summary>
    [TestFixture]
    public sealed class EasySlackNunitAllTests : TouchstoneNunitBase
    {
        /// <summary>
        /// Gets the suites exercised by this host.
        /// </summary>
        protected override IReadOnlyList<TestSuiteDescriptor> Suites
        {
            get { return EasySlackTestSuites.All; }
        }

        /// <summary>
        /// Runs all descriptors.
        /// </summary>
        /// <returns>A task that completes when every case has run.</returns>
        [Test]
        public async Task RunAll()
        {
            await RunAllAsync();
        }
    }
}
