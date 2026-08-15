namespace Test.Shared
{
    using System.Collections.Generic;
    using Test.Shared.Suites;
    using Touchstone.Core;

    /// <summary>
    /// Central source of truth for the EasySlack test suites. Every runner -- the Touchstone console
    /// runner, the xUnit adapter, and the NUnit adapter -- executes exactly these descriptors.
    /// </summary>
    public static class EasySlackTestSuites
    {
        /// <summary>
        /// Gets every EasySlack test suite as Touchstone descriptors.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor>
                {
                    AuthMaterialSuite.Build(),
                    OptionsSuite.Build(),
                    WebApiSuite.Build(),
                    LifecycleSuite.Build(),
                    SocketProcessingSuite.Build(),
                    EnvelopeProcessorSuite.Build(),
                };
            }
        }
    }
}
