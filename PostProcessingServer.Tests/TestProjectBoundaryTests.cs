using System;
using PostProcessingServer.Services;
using PostProcessingServer.Services.Analytics;
using Xunit;

namespace PostProcessingServer.Tests
{
    public sealed class TestProjectBoundaryTests
    {
        [Fact]
        public void HostTestsExerciseProductionTypesRatherThanLinkedSourceCopies()
        {
            Assert.Equal("PostProcessingServer", typeof(AnalysisJobQueue).Assembly.GetName().Name);
            Assert.Same(typeof(AnalysisJobQueue).Assembly, typeof(TrafficMath).Assembly);
        }

        [Fact]
        public void HostTestsDoNotDependOnAnotherTestSuite()
        {
            Assert.DoesNotContain(typeof(TestProjectBoundaryTests).Assembly.GetReferencedAssemblies(),
                reference => reference.Name.EndsWith(".Tests", StringComparison.Ordinal));
        }
    }
}
