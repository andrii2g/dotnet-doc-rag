using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocRag.Core.Documents;
using FluentAssertions;

namespace DocRag.IntegrationTests;

public sealed class DocumentReadEndpointTests : IClassFixture<ApiTestApplicationFactory>
{
    private readonly ApiTestApplicationFactory _factory;

    public DocumentReadEndpointTests(ApiTestApplicationFactory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task ListDocuments_ShouldReturnPagedVisibleDocuments()
    {
        var now = DateTimeOffset.UtcNow;
        _factory.SeedDocument(CreateDocument(Guid.NewGuid(), "alpha.txt", now.AddMinutes(-2), deletedAt: null));
        _factory.SeedDocument(CreateDocument(Guid.NewGuid(), "beta.txt", now.AddMinutes(-1), deletedAt: null));
        _factory.SeedDocument(CreateDocument(Guid.NewGuid(), "deleted.txt", now, deletedAt: now));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var response = await client.GetAsync("/api/documents?limit=2&offset=0");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.GetProperty("items").GetArrayLength().Should().Be(2);
        payload.GetProperty("limit").GetInt32().Should().Be(2);
        payload.GetProperty("offset").GetInt32().Should().Be(0);
        payload.GetProperty("items")[0].GetProperty("fileName").GetString().Should().Be("beta.txt");
        payload.GetProperty("items")[1].GetProperty("fileName").GetString().Should().Be("alpha.txt");
    }

    [Fact]
    public async Task ListDocuments_ShouldRejectInvalidPaging()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var response = await client.GetAsync("/api/documents?limit=0&offset=-1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDocument_ShouldReturnNotFound_ForMissingOrDeletedDocument()
    {
        var deletedId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _factory.SeedDocument(CreateDocument(deletedId, "deleted.txt", now, deletedAt: now));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var missingResponse = await client.GetAsync($"/api/documents/{Guid.NewGuid()}");
        var deletedResponse = await client.GetAsync($"/api/documents/{deletedId}");

        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        deletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDocument_ShouldReturnDocumentDetails()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _factory.SeedDocument(CreateDocument(id, "policy.txt", now, deletedAt: null));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var response = await client.GetAsync($"/api/documents/{id}");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.GetProperty("id").GetGuid().Should().Be(id);
        payload.GetProperty("fileName").GetString().Should().Be("policy.txt");
        payload.GetProperty("status").GetString().Should().Be("indexed");
        payload.GetProperty("chunkCount").GetInt32().Should().Be(3);
    }

    private static DocumentRecord CreateDocument(Guid id, string fileName, DateTimeOffset createdAt, DateTimeOffset? deletedAt)
    {
        return new DocumentRecord(
            id,
            fileName,
            fileName,
            ".txt",
            "text/plain",
            DocumentSourceType.Upload,
            null,
            128,
            $"{id:N}-sha",
            DocumentStatus.Indexed,
            null,
            3,
            "openai",
            "text-embedding-3-small",
            1536,
            createdAt,
            createdAt,
            createdAt,
            deletedAt);
    }
}
