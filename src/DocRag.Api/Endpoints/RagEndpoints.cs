using DocRag.Core.Abstractions;
using DocRag.Core.Rag;
using DocRag.Core.Retrieval;
using DocRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocRag.Api.Endpoints;

public static class RagEndpoints
{
    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/rag/search", SearchAsync);
        endpoints.MapPost("/api/rag/ask", AskAsync);
        return endpoints;
    }

    private static async Task<IResult> SearchAsync(
        SearchRequest request,
        IEmbeddingClient embeddingClient,
        IChunkRepository chunkRepository,
        IOptions<RagOptions> ragOptions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Trim().Length > 4000)
        {
            return Results.BadRequest(new ApiError("ValidationError", "Request validation failed.",
                new Dictionary<string, string> { ["query"] = "query must be between 1 and 4000 characters." }));
        }

        var bounds = ApiRequestValidation.ValidateSearchBounds(request.TopK, request.CandidateK, request.DocumentIds, ragOptions.Value);
        if (bounds.Error is not null)
        {
            return Results.BadRequest(bounds.Error);
        }

        var queryEmbedding = await embeddingClient.EmbedQueryAsync(request.Query.Trim(), cancellationToken);
        var results = await chunkRepository.SearchAsync(
            new RetrievalQuery(queryEmbedding, bounds.ResolvedCandidateK, ragOptions.Value.HnswEfSearch, bounds.DocumentIds),
            cancellationToken);

        var filtered = results
            .Where(result => result.SimilarityScore >= ragOptions.Value.MinSimilarity)
            .Take(bounds.ResolvedTopK)
            .ToArray();

        return Results.Ok(new
        {
            query = request.Query.Trim(),
            resolvedTopK = bounds.ResolvedTopK,
            resolvedCandidateK = bounds.ResolvedCandidateK,
            results = filtered.Select(result => new
            {
                chunkId = result.ChunkId,
                documentId = result.DocumentId,
                fileName = result.FileName,
                chunkIndex = result.ChunkIndex,
                pageStart = result.PageStart,
                pageEnd = result.PageEnd,
                heading = result.Heading,
                similarityScore = result.SimilarityScore,
                contentPreview = result.Content.Length > 120 ? $"{result.Content[..120]}..." : result.Content
            })
        });
    }

    private static async Task<IResult> AskAsync(
        AskRequest request,
        IEmbeddingClient embeddingClient,
        IChunkRepository chunkRepository,
        IRagAnswerGenerator ragAnswerGenerator,
        IOptions<RagOptions> ragOptions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Trim().Length > 4000)
        {
            return Results.BadRequest(new ApiError("ValidationError", "Request validation failed.",
                new Dictionary<string, string> { ["question"] = "question must be between 1 and 4000 characters." }));
        }

        var bounds = ApiRequestValidation.ValidateSearchBounds(request.TopK, request.CandidateK, request.DocumentIds, ragOptions.Value);
        if (bounds.Error is not null)
        {
            return Results.BadRequest(bounds.Error);
        }

        var queryEmbedding = await embeddingClient.EmbedQueryAsync(request.Question.Trim(), cancellationToken);
        var retrieved = await chunkRepository.SearchAsync(
            new RetrievalQuery(queryEmbedding, bounds.ResolvedCandidateK, ragOptions.Value.HnswEfSearch, bounds.DocumentIds),
            cancellationToken);

        var filtered = retrieved
            .Where(result => result.SimilarityScore >= ragOptions.Value.MinSimilarity)
            .Take(bounds.ResolvedTopK)
            .ToArray();

        if (filtered.Length == 0)
        {
            return Results.Ok(new
            {
                answer = "I don't know.",
                citations = Array.Empty<object>(),
                resolvedTopK = bounds.ResolvedTopK,
                resolvedCandidateK = bounds.ResolvedCandidateK,
                context = request.IncludeContext == true ? Array.Empty<object>() : null
            });
        }

        var answer = await ragAnswerGenerator.GenerateAsync(
            new RagAnswerRequest(request.Question.Trim(), filtered, request.IncludeContext ?? false),
            cancellationToken);

        return Results.Ok(new
        {
            answer = answer.Answer,
            citations = answer.Citations.Select(citation => new
            {
                sourceId = citation.SourceId,
                documentId = citation.DocumentId,
                fileName = citation.FileName,
                chunkIndex = citation.ChunkIndex,
                pageStart = citation.PageStart,
                pageEnd = citation.PageEnd,
                heading = citation.Heading,
                similarityScore = citation.SimilarityScore
            }),
            resolvedTopK = bounds.ResolvedTopK,
            resolvedCandidateK = bounds.ResolvedCandidateK,
            context = request.IncludeContext == true
                ? answer.Context.Select(context => new
                {
                    sourceId = context.SourceId,
                    documentId = context.DocumentId,
                    fileName = context.FileName,
                    chunkIndex = context.ChunkIndex,
                    pageStart = context.PageStart,
                    pageEnd = context.PageEnd,
                    heading = context.Heading,
                    similarityScore = context.SimilarityScore,
                    content = context.Content
                })
                : null
        });
    }

    private sealed record SearchRequest(string Query, int? TopK, int? CandidateK, IReadOnlyList<Guid>? DocumentIds);
    private sealed record AskRequest(string Question, int? TopK, int? CandidateK, IReadOnlyList<Guid>? DocumentIds, bool? IncludeContext);
}
