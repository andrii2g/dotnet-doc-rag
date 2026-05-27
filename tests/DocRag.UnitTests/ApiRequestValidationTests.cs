using DocRag.Api;
using DocRag.Infrastructure.Configuration;
using FluentAssertions;

namespace DocRag.UnitTests;

public sealed class ApiRequestValidationTests
{
    private static readonly RagOptions Options = new()
    {
        DefaultTopK = 8,
        MaxTopK = 20,
        DefaultCandidateK = 24,
        MaxCandidateK = 100
    };

    [Fact]
    public void ValidateSearchBounds_ShouldRejectInvalidTopK()
    {
        var result = ApiRequestValidation.ValidateSearchBounds(0, null, null, Options);

        result.Error.Should().NotBeNull();
        result.Error!.Error.Should().Be("ValidationError");
    }

    [Fact]
    public void ValidateSearchBounds_ShouldRejectCandidateKLessThanTopK()
    {
        var result = ApiRequestValidation.ValidateSearchBounds(10, 5, null, Options);

        result.Error.Should().NotBeNull();
        result.Error!.Details.Should().NotBeNull();
    }

    [Fact]
    public void ValidateSearchBounds_ShouldResolveDefaults()
    {
        var result = ApiRequestValidation.ValidateSearchBounds(10, null, null, Options);

        result.Error.Should().BeNull();
        result.ResolvedTopK.Should().Be(10);
        result.ResolvedCandidateK.Should().Be(24);
    }
}
