using DocRag.Core.Documents;

namespace DocRag.Core.Abstractions;

public interface IIngestionJobRepository
{
    Task CreateAsync(Guid documentId, CancellationToken cancellationToken);
    Task<IngestionJobRecord?> ClaimNextAsync(string workerId, CancellationToken cancellationToken);
    Task MarkSucceededAsync(Guid jobId, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid jobId, string errorMessage, bool retryable, CancellationToken cancellationToken);
}
