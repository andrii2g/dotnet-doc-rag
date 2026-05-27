using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DocRag.Core.Abstractions;
using DocRag.Core.Embeddings;
using DocRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocRag.Infrastructure.Embeddings;

public sealed class OpenAiEmbeddingClient(
    IHttpClientFactory httpClientFactory,
    IOptions<AiOptions> aiOptions) : IEmbeddingClient
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly AiOptions _aiOptions = aiOptions.Value;

    public async Task<float[]> EmbedQueryAsync(string input, CancellationToken cancellationToken)
    {
        var embeddings = await EmbedDocumentsAsync([input], cancellationToken);
        return embeddings[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        if (inputs.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(_aiOptions.OpenAIApiKey))
        {
            throw new InvalidOperationException("AI:OpenAIApiKey must be configured.");
        }

        var client = _httpClientFactory.CreateClient("openai");
        using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = JsonContent.Create(new EmbeddingRequest(EmbeddingDefaults.Model, inputs))
        };

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _aiOptions.OpenAIApiKey);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenAI embedding response was empty.");

        var orderedEmbeddings = payload.Data
            .OrderBy(item => item.Index)
            .Select(item => item.Embedding)
            .ToArray();

        foreach (var embedding in orderedEmbeddings)
        {
            if (embedding.Length != EmbeddingDefaults.Dimensions)
            {
                throw new InvalidOperationException(
                    "Embedding dimension mismatch. Expected 1536 values for text-embedding-3-small.");
            }
        }

        return orderedEmbeddings;
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] EmbeddingResponseItem[] Data);

    private sealed record EmbeddingResponseItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
