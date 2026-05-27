using DocRag.Core.Documents;

namespace DocRag.Core.Abstractions;

public interface IManagedFileStorage
{
    Task<StoredFileResult> StoreUploadAsync(Stream source, string originalFileName, string? contentType, CancellationToken cancellationToken);
    Task<StoredFileResult> StoreImportFileAsync(string sourcePath, CancellationToken cancellationToken);
    string CreateStoredFileName(Guid documentId, string extension);
    Task PromoteTempFileAsync(string tempFilePath, string storedFileName, CancellationToken cancellationToken);
    Task DeleteManagedFileAsync(string storedFileName, CancellationToken cancellationToken);
    string GetManagedPath(string storedFileName);
}
