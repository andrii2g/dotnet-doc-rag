using DocRag.Core.Retrieval;

namespace DocRag.Core.Abstractions;

public interface IChunkRepository
{
    Task ReplaceChunksAsync(Guid documentId, IReadOnlyList<DocumentChunkToInsert> chunks, CancellationToken cancellationToken);
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(RetrievalQuery query, CancellationToken cancellationToken);
    Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken);
}
