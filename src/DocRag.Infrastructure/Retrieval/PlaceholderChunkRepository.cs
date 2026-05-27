using DocRag.Core.Abstractions;
using DocRag.Core.Retrieval;

namespace DocRag.Infrastructure.Retrieval;

public sealed class PlaceholderChunkRepository : IChunkRepository
{
    public Task ReplaceChunksAsync(Guid documentId, IReadOnlyList<DocumentChunkToInsert> chunks, CancellationToken cancellationToken)
        => throw new NotImplementedException("Chunk persistence is implemented in a later step.");

    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(RetrievalQuery query, CancellationToken cancellationToken)
        => throw new NotImplementedException("Chunk retrieval is implemented in a later step.");

    public Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken)
        => throw new NotImplementedException("Chunk deletion is implemented in a later step.");
}
