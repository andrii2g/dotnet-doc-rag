using FluentAssertions;

namespace DocRag.IntegrationTests;

public sealed class SmokeTests
{
    [Fact]
    public void SolutionSmokeTest_ShouldRun()
    {
        true.Should().BeTrue();
    }
}
