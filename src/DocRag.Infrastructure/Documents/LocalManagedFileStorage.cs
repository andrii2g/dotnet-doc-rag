using System.Security.Cryptography;
using DocRag.Core.Abstractions;
using DocRag.Core.Documents;
using DocRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocRag.Infrastructure.Documents;

public sealed class LocalManagedFileStorage(IOptions<AppOptions> appOptions) : IManagedFileStorage
{
    private readonly AppOptions _appOptions = appOptions.Value;

    public async Task<StoredFileResult> StoreUploadAsync(
        Stream source,
        string originalFileName,
        string? contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);

        var extension = Path.GetExtension(originalFileName);
        var tempFilePath = GetTempFilePath();
        Directory.CreateDirectory(GetTempRoot());

        var (sizeBytes, sha256) = await CopyToFileAndHashAsync(source, tempFilePath, cancellationToken);

        return new StoredFileResult(
            originalFileName,
            tempFilePath,
            extension,
            contentType,
            sizeBytes,
            sha256,
            null);
    }

    public async Task<StoredFileResult> StoreImportFileAsync(string sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var importRoot = Path.GetFullPath(_appOptions.ImportPath);

        if (!fullSourcePath.StartsWith(importRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Import file path escapes the configured import root.");
        }

        Directory.CreateDirectory(GetTempRoot());

        var extension = Path.GetExtension(fullSourcePath);
        var tempFilePath = GetTempFilePath();
        await using var sourceStream = File.OpenRead(fullSourcePath);
        var (sizeBytes, sha256) = await CopyToFileAndHashAsync(sourceStream, tempFilePath, cancellationToken);

        var relativeSourcePath = Path.GetRelativePath(importRoot, fullSourcePath);
        return new StoredFileResult(
            Path.GetFileName(fullSourcePath),
            tempFilePath,
            extension,
            null,
            sizeBytes,
            sha256,
            relativeSourcePath);
    }

    public Task DeleteManagedFileAsync(string storedFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var managedPath = GetManagedPath(storedFileName);
        if (File.Exists(managedPath))
        {
            File.Delete(managedPath);
        }

        return Task.CompletedTask;
    }

    public string GetManagedPath(string storedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedFileName);

        var storageRoot = Path.GetFullPath(_appOptions.StoragePath);
        var managedPath = Path.GetFullPath(Path.Combine(storageRoot, storedFileName));

        if (!managedPath.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Managed file path escapes storage root.");
        }

        return managedPath;
    }

    public string CreateStoredFileName(Guid documentId, string extension) => $"{documentId:D}{extension}";

    public Task PromoteTempFileAsync(string tempFilePath, string storedFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetPath = GetManagedPath(storedFileName);
        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Managed file path must have a directory.");

        Directory.CreateDirectory(targetDirectory);

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempFilePath, targetPath);
        return Task.CompletedTask;
    }

    private string GetTempRoot()
    {
        var storageRoot = Path.GetFullPath(_appOptions.StoragePath);
        var storageParent = Directory.GetParent(storageRoot)?.FullName
            ?? throw new InvalidOperationException("Storage path must have a parent directory.");
        return Path.Combine(storageParent, "tmp");
    }

    private string GetTempFilePath() => Path.Combine(GetTempRoot(), $"{Path.GetRandomFileName()}.tmp");

    private static async Task<(long SizeBytes, string Sha256)> CopyToFileAndHashAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var destination = File.Create(destinationPath);
        using var sha256 = SHA256.Create();

        var buffer = new byte[81920];
        long totalBytes = 0;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            totalBytes += bytesRead;
        }

        sha256.TransformFinalBlock([], 0, 0);
        return (totalBytes, Convert.ToHexString(sha256.Hash!).ToLowerInvariant());
    }
}
