using DocRag.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace DocRag.UnitTests;

public sealed class ApiKeyRequestPolicyTests
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public void AllowsAnonymous_ShouldAlwaysAllowHealthRoutes(string path)
    {
        ApiKeyRequestPolicy.AllowsAnonymous(new PathString(path), isDevelopment: false).Should().BeTrue();
        ApiKeyRequestPolicy.AllowsAnonymous(new PathString(path), isDevelopment: true).Should().BeTrue();
    }

    [Theory]
    [InlineData("/docs")]
    [InlineData("/docs/index.html")]
    [InlineData("/openapi/v1.json")]
    public void AllowsAnonymous_ShouldAllowDocsOnlyInDevelopment(string path)
    {
        ApiKeyRequestPolicy.AllowsAnonymous(new PathString(path), isDevelopment: true).Should().BeTrue();
        ApiKeyRequestPolicy.AllowsAnonymous(new PathString(path), isDevelopment: false).Should().BeFalse();
    }

    [Theory]
    [InlineData("/api/documents")]
    [InlineData("/api/rag/ask")]
    [InlineData("/")]
    public void AllowsAnonymous_ShouldRejectProtectedRoutes(string path)
    {
        ApiKeyRequestPolicy.AllowsAnonymous(new PathString(path), isDevelopment: false).Should().BeFalse();
        ApiKeyRequestPolicy.AllowsAnonymous(new PathString(path), isDevelopment: true).Should().BeFalse();
    }
}
