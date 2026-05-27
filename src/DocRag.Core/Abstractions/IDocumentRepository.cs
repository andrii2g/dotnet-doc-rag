using DocRag.Core.Documents;

namespace DocRag.Core.Abstractions;

public interface IDocumentRepository
{
    Task<DocumentRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<DocumentRecord?> GetActiveByHashAsync(string sha256, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentRecord>> ListAsync(int limit, int offset, CancellationToken cancellationToken);
    Task<DocumentRecord> CreateQueuedAsync(CreateDocumentCommand command, CancellationToken cancellationToken);
    Task MarkProcessingAsync(Guid id, CancellationToken cancellationToken);
    Task MarkIndexedAsync(Guid id, int chunkCount, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid id, string errorMessage, CancellationToken cancellationToken);
    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken);
}
