using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocRag.Core.Rag;
using DocRag.Core.Retrieval;
using FluentAssertions;

namespace DocRag.IntegrationTests;

public sealed class RagEndpointTests : IClassFixture<ApiTestApplicationFactory>
{
    private readonly ApiTestApplicationFactory _factory;

    public RagEndpointTests(ApiTestApplicationFactory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task Search_ShouldRejectInvalidBounds()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var response = await client.PostAsJsonAsync("/api/rag/search", new
        {
            query = "hello",
            topK = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_ShouldFilterBySimilarity_AndTrimPreview()
    {
        _factory.ChunkRepository.SearchResults =
        [
            new RetrievedChunk(Guid.NewGuid(), Guid.NewGuid(), "guide.txt", 0, new string('a', 150), 1, 1, "Intro", 0.91),
            new RetrievedChunk(Guid.NewGuid(), Guid.NewGuid(), "guide.txt", 1, "low similarity", 1, 1, "Body", 0.10)
        ];

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var response = await client.PostAsJsonAsync("/api/rag/search", new
        {
            query = "  hello world  ",
            topK = 2,
            candidateK = 3
        });
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.EmbeddingClient.Queries.Should().ContainSingle().Which.Should().Be("hello world");
        _factory.ChunkRepository.LastQuery.Should().NotBeNull();
        _factory.ChunkRepository.LastQuery!.CandidateK.Should().Be(3);
        payload.GetProperty("results").GetArrayLength().Should().Be(1);
        payload.GetProperty("results")[0].GetProperty("contentPreview").GetString()!.Length.Should().Be(123);
    }

    [Fact]
    public async Task Ask_ShouldReturnIDontKnow_WhenNoChunksMatch()
    {
        _factory.ChunkRepository.SearchResults = [];

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var response = await client.PostAsJsonAsync("/api/rag/ask", new
        {
            question = "What is the retention period?"
        });
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.GetProperty("answer").GetString().Should().Be("I don't know.");
        payload.GetProperty("citations").GetArrayLength().Should().Be(0);
        _factory.RagAnswerGenerator.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task Ask_ShouldUseRagGenerator_AndReturnContext_WhenRequested()
    {
        var documentId = Guid.NewGuid();
        var retrieved = new RetrievedChunk(Guid.NewGuid(), documentId, "policy.txt", 4, "Retention is 30 days.", 2, 2, "Retention", 0.88);

        _factory.ChunkRepository.SearchResults = [retrieved];
        _factory.RagAnswerGenerator.Response = new RagAnswer(
            "Retention is 30 days. [1]",
            [new RagCitation(1, documentId, "policy.txt", 4, 2, 2, "Retention", 0.88)],
            [new RagContextItem(1, documentId, "policy.txt", 4, 2, 2, "Retention", 0.88, "Retention is 30 days.")]);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var response = await client.PostAsJsonAsync("/api/rag/ask", new
        {
            question = "What is the retention period?",
            includeContext = true
        });
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.RagAnswerGenerator.LastRequest.Should().NotBeNull();
        _factory.RagAnswerGenerator.LastRequest!.Question.Should().Be("What is the retention period?");
        _factory.RagAnswerGenerator.LastRequest.IncludeContext.Should().BeTrue();
        payload.GetProperty("answer").GetString().Should().Be("Retention is 30 days. [1]");
        payload.GetProperty("citations").GetArrayLength().Should().Be(1);
        payload.GetProperty("context").GetArrayLength().Should().Be(1);
    }
}
