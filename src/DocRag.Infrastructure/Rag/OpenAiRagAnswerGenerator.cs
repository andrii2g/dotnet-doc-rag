using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using DocRag.Core.Abstractions;
using DocRag.Core.Rag;
using DocRag.Core.Retrieval;
using DocRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocRag.Infrastructure.Rag;

public sealed class OpenAiRagAnswerGenerator(
    IHttpClientFactory httpClientFactory,
    IOptions<AiOptions> aiOptions) : IRagAnswerGenerator
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly AiOptions _aiOptions = aiOptions.Value;

    public async Task<RagAnswer> GenerateAsync(RagAnswerRequest request, CancellationToken cancellationToken)
    {
        if (request.RetrievedChunks.Count == 0)
        {
            return new RagAnswer("I don't know.", [], []);
        }

        if (string.IsNullOrWhiteSpace(_aiOptions.OpenAIApiKey))
        {
            throw new InvalidOperationException("AI:OpenAIApiKey must be configured.");
        }

        var context = BuildContext(request.RetrievedChunks);
        var prompt = BuildPrompt(request.Question, context);

        var client = _httpClientFactory.CreateClient("openai");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(new ChatCompletionsRequest(
                _aiOptions.ChatModel,
                [
                    new ChatMessage("system", """
                    You answer questions using only the provided context.
                    If the context does not contain enough information to answer, respond exactly: I don't know.
                    Do not use outside knowledge.
                    Cite sources using bracketed source numbers like [1].
                    Do not cite a source unless it directly supports the sentence.
                    Keep the answer concise and factual.
                    """),
                    new ChatMessage("user", prompt)
                ]))
        };

        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _aiOptions.OpenAIApiKey);

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenAI chat response was empty.");

        var answerText = payload.Choices.FirstOrDefault()?.Message.Content?.Trim();
        if (string.IsNullOrWhiteSpace(answerText))
        {
            answerText = "I don't know.";
        }

        var citations = BuildCitations(answerText, request.RetrievedChunks);
        if (answerText != "I don't know." && citations.Count == 0)
        {
            answerText = "I don't know.";
        }

        var responseContext = request.IncludeContext
            ? context.Select(item => new RagContextItem(
                item.SourceId,
                item.DocumentId,
                item.FileName,
                item.ChunkIndex,
                item.PageStart,
                item.PageEnd,
                item.Heading,
                item.SimilarityScore,
                item.Content)).ToArray()
            : [];

        return new RagAnswer(answerText, citations, responseContext);
    }

    private static List<RagCitation> BuildCitations(string answer, IReadOnlyList<RetrievedChunk> chunks)
    {
        var citations = new List<RagCitation>();
        for (var index = 0; index < chunks.Count; index++)
        {
            var sourceId = index + 1;
            if (!answer.Contains($"[{sourceId}]", StringComparison.Ordinal))
            {
                continue;
            }

            var chunk = chunks[index];
            citations.Add(new RagCitation(
                sourceId,
                chunk.DocumentId,
                chunk.FileName,
                chunk.ChunkIndex,
                chunk.PageStart,
                chunk.PageEnd,
                chunk.Heading,
                chunk.SimilarityScore));
        }

        return citations;
    }

    private static string BuildPrompt(string question, IReadOnlyList<ContextItem> context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Question:");
        builder.AppendLine(question);
        builder.AppendLine();
        builder.AppendLine("Context:");

        foreach (var item in context)
        {
            builder.AppendLine($"[{item.SourceId}]");
            builder.AppendLine($"Document: {item.FileName}");
            builder.AppendLine($"Chunk: {item.ChunkIndex}");
            builder.AppendLine($"Page: {(item.PageStart is null ? "n/a" : item.PageStart)}");
            builder.AppendLine($"Heading: {item.Heading ?? "n/a"}");
            builder.AppendLine("Text:");
            builder.AppendLine(item.Content);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IReadOnlyList<ContextItem> BuildContext(IReadOnlyList<RetrievedChunk> chunks)
    {
        return chunks.Select((chunk, index) => new ContextItem(
            index + 1,
            chunk.DocumentId,
            chunk.FileName,
            chunk.ChunkIndex,
            chunk.PageStart,
            chunk.PageEnd,
            chunk.Heading,
            chunk.SimilarityScore,
            chunk.Content)).ToArray();
    }

    private sealed record ContextItem(
        int SourceId,
        Guid DocumentId,
        string FileName,
        int ChunkIndex,
        int? PageStart,
        int? PageEnd,
        string? Heading,
        double SimilarityScore,
        string Content);

    private sealed record ChatCompletionsRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatCompletionsResponse(
        [property: JsonPropertyName("choices")] ChatChoice[] Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage Message);
}
