using DocRag.Core.Abstractions;
using DocRag.Core.Rag;

namespace DocRag.Infrastructure.Rag;

public sealed class PlaceholderRagAnswerGenerator : IRagAnswerGenerator
{
    public Task<RagAnswer> GenerateAsync(RagAnswerRequest request, CancellationToken cancellationToken)
        => throw new NotImplementedException("RAG answer generation is implemented in a later step.");
}
