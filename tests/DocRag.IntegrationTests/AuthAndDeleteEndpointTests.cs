using System.Net;
using DocRag.Core.Documents;
using FluentAssertions;

namespace DocRag.IntegrationTests;

public sealed class AuthAndDeleteEndpointTests : IClassFixture<ApiTestApplicationFactory>
{
    private readonly ApiTestApplicationFactory _factory;

    public AuthAndDeleteEndpointTests(ApiTestApplicationFactory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task HealthEndpoint_ShouldAllowAnonymous_WhenApiKeyIsEnabled()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DocsEndpoint_ShouldRequireApiKey_InTestingEnvironment()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/docs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteDocument_ShouldBeIdempotent_AndDeleteManagedArtifactsOnce()
    {
        var documentId = Guid.NewGuid();
        _factory.SeedDocument(CreateDocument(documentId, "stored/file.txt"));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var firstResponse = await client.DeleteAsync($"/api/documents/{documentId}");
        var secondResponse = await client.DeleteAsync($"/api/documents/{documentId}");

        firstResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.ChunkRepository.DeletedDocumentIds.Should().ContainSingle().Which.Should().Be(documentId);
        _factory.ManagedFileStorage.DeletedStoredFileNames.Should().ContainSingle().Which.Should().Be("stored/file.txt");
    }

    [Fact]
    public async Task DeleteUnknownDocument_ShouldReturnNoContent_WithoutTouchingStorage()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var response = await client.DeleteAsync($"/api/documents/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.ChunkRepository.DeletedDocumentIds.Should().BeEmpty();
        _factory.ManagedFileStorage.DeletedStoredFileNames.Should().BeEmpty();
    }

    private static DocumentRecord CreateDocument(Guid id, string storedFileName)
    {
        var now = DateTimeOffset.UtcNow;

        return new DocumentRecord(
            id,
            "doc.txt",
            storedFileName,
            ".txt",
            "text/plain",
            DocumentSourceType.Upload,
            null,
            12,
            "sha256",
            DocumentStatus.Indexed,
            null,
            1,
            "openai",
            "text-embedding-3-small",
            1536,
            now,
            now,
            now,
            null);
    }
}
