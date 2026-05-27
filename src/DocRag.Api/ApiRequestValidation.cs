using DocRag.Infrastructure.Configuration;

namespace DocRag.Api;

public static class ApiRequestValidation
{
    public static (int ResolvedTopK, int ResolvedCandidateK, Guid[] DocumentIds, ApiError? Error) ValidateSearchBounds(
        int? topK,
        int? candidateK,
        IReadOnlyList<Guid>? documentIds,
        RagOptions options)
    {
        var resolvedTopK = topK ?? options.DefaultTopK;
        if (resolvedTopK < 1 || resolvedTopK > options.MaxTopK)
        {
            return (0, 0, [], new ApiError("ValidationError", "Request validation failed.",
                new Dictionary<string, string> { ["topK"] = $"topK must be between 1 and {options.MaxTopK}." }));
        }

        var resolvedCandidateK = candidateK ?? Math.Max(options.DefaultCandidateK, resolvedTopK);
        if (resolvedCandidateK < resolvedTopK || resolvedCandidateK > options.MaxCandidateK)
        {
            return (0, 0, [], new ApiError("ValidationError", "Request validation failed.",
                new Dictionary<string, string> { ["candidateK"] = $"candidateK must be between resolved topK and {options.MaxCandidateK}." }));
        }

        var normalizedDocumentIds = documentIds?
            .Distinct()
            .ToArray() ?? [];

        if (normalizedDocumentIds.Length > 50)
        {
            return (0, 0, [], new ApiError("ValidationError", "Request validation failed.",
                new Dictionary<string, string> { ["documentIds"] = "documentIds must contain at most 50 unique values." }));
        }

        return (resolvedTopK, resolvedCandidateK, normalizedDocumentIds, null);
    }
}
