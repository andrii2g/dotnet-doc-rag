using DocRag.Core.Rag;

namespace DocRag.Core.Abstractions;

public interface IRagAnswerGenerator
{
    Task<RagAnswer> GenerateAsync(RagAnswerRequest request, CancellationToken cancellationToken);
}
