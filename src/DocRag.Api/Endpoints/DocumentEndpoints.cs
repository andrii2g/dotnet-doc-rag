using DocRag.Core.Abstractions;
using DocRag.Core.Documents;
using DocRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocRag.Api.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/documents/upload", UploadDocumentAsync);
        endpoints.MapPost("/api/documents/import-folder", ImportFolderAsync);
        endpoints.MapGet("/api/documents", ListDocumentsAsync);
        endpoints.MapGet("/api/documents/{id:guid}", GetDocumentAsync);
        endpoints.MapDelete("/api/documents/{id:guid}", DeleteDocumentAsync);

        return endpoints;
    }

    private static async Task<IResult> UploadDocumentAsync(
        HttpRequest request,
        IDocumentRepository documentRepository,
        IIngestionJobRepository jobRepository,
        IManagedFileStorage managedFileStorage,
        IOptions<AppOptions> appOptions,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new ApiError("ValidationError", "Request validation failed.",
                new Dictionary<string, string> { ["file"] = "Multipart form-data is required." }));
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"];
        if (file is null)
        {
            return Results.BadRequest(new ApiError("ValidationError", "Request validation failed.",
                new Dictionary<string, string> { ["file"] = "file is required." }));
        }

        var validationError = ValidateUpload(file.FileName, file.Length, appOptions.Value);
        if (validationError is not null)
        {
            return Results.BadRequest(validationError);
        }

        await using var stream = file.OpenReadStream();
        var staged = await managedFileStorage.StoreUploadAsync(stream, file.FileName, file.ContentType, cancellationToken);

        var existing = await documentRepository.GetActiveByHashAsync(staged.ContentSha256, cancellationToken);
        if (existing is not null)
        {
            File.Delete(staged.TempFilePath);
            return Results.Ok(new
            {
                documentId = existing.Id,
                fileName = existing.OriginalFileName,
                status = "indexed",
                duplicate = true
            });
        }

        var documentId = Guid.NewGuid();
        var storedFileName = managedFileStorage.CreateStoredFileName(documentId, staged.Extension);
        await managedFileStorage.PromoteTempFileAsync(staged.TempFilePath, storedFileName, cancellationToken);

        var document = await documentRepository.CreateQueuedAsync(
            new CreateDocumentCommand(
                documentId,
                staged.OriginalFileName,
                storedFileName,
                staged.Extension,
                staged.ContentType,
                DocumentSourceType.Upload,
                null,
                staged.SizeBytes,
                staged.ContentSha256),
            cancellationToken);

        await jobRepository.CreateAsync(document.Id, cancellationToken);

        return Results.Accepted($"/api/documents/{document.Id}", new
        {
            documentId = document.Id,
            fileName = document.OriginalFileName,
            status = "queued",
            duplicate = false
        });
    }

    private static async Task<IResult> ImportFolderAsync(
        ImportFolderRequest request,
        IDocumentRepository documentRepository,
        IIngestionJobRepository jobRepository,
        IManagedFileStorage managedFileStorage,
        IOptions<AppOptions> appOptions,
        CancellationToken cancellationToken)
    {
        var importRoot = Path.GetFullPath(appOptions.Value.ImportPath);
        if (!Directory.Exists(importRoot))
        {
            return Results.Ok(new { queued = 0, skipped = 0, failed = 0, documents = Array.Empty<object>(), skippedFiles = Array.Empty<object>() });
        }

        var searchOption = request.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(importRoot, "*", searchOption);

        var queuedDocuments = new List<object>();
        var skippedFiles = new List<object>();
        var failed = 0;

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSymlinkOrReparsePoint(filePath))
            {
                failed++;
                continue;
            }

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (!appOptions.Value.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var staged = await managedFileStorage.StoreImportFileAsync(filePath, cancellationToken);
                var existing = await documentRepository.GetActiveByHashAsync(staged.ContentSha256, cancellationToken);
                if (existing is not null)
                {
                    File.Delete(staged.TempFilePath);
                    skippedFiles.Add(new { fileName = staged.OriginalFileName, reason = "DuplicateContent" });
                    continue;
                }

                var documentId = Guid.NewGuid();
                var storedFileName = managedFileStorage.CreateStoredFileName(documentId, staged.Extension);
                await managedFileStorage.PromoteTempFileAsync(staged.TempFilePath, storedFileName, cancellationToken);

                var document = await documentRepository.CreateQueuedAsync(
                    new CreateDocumentCommand(
                        documentId,
                        staged.OriginalFileName,
                        storedFileName,
                        staged.Extension,
                        staged.ContentType,
                        DocumentSourceType.Import,
                        staged.SourcePath,
                        staged.SizeBytes,
                        staged.ContentSha256),
                    cancellationToken);

                await jobRepository.CreateAsync(document.Id, cancellationToken);

                queuedDocuments.Add(new
                {
                    documentId = document.Id,
                    fileName = document.OriginalFileName,
                    status = "queued",
                    duplicate = false
                });
            }
            catch
            {
                failed++;
            }
        }

        return Results.Ok(new
        {
            queued = queuedDocuments.Count,
            skipped = skippedFiles.Count,
            failed,
            documents = queuedDocuments,
            skippedFiles
        });
    }

    private static async Task<IResult> ListDocumentsAsync(
        int? limit,
        int? offset,
        IDocumentRepository documentRepository,
        CancellationToken cancellationToken)
    {
        var resolvedLimit = limit ?? 50;
        var resolvedOffset = offset ?? 0;

        if (resolvedLimit < 1 || resolvedLimit > 100 || resolvedOffset < 0)
        {
            return Results.BadRequest(new ApiError("ValidationError", "Request validation failed.", new Dictionary<string, string>
            {
                ["limit"] = "limit must be between 1 and 100.",
                ["offset"] = "offset must be 0 or greater."
            }));
        }

        var items = await documentRepository.ListAsync(resolvedLimit, resolvedOffset, cancellationToken);
        return Results.Ok(new
        {
            items = items.Select(item => new
            {
                id = item.Id,
                fileName = item.OriginalFileName,
                sourceType = item.SourceType.ToString().ToLowerInvariant(),
                status = item.Status.ToString().ToLowerInvariant(),
                chunkCount = item.ChunkCount,
                createdAt = item.CreatedAt,
                indexedAt = item.IndexedAt
            }),
            limit = resolvedLimit,
            offset = resolvedOffset
        });
    }

    private static async Task<IResult> GetDocumentAsync(
        Guid id,
        IDocumentRepository documentRepository,
        CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null || document.DeletedAt is not null)
        {
            return Results.NotFound(new ApiError("NotFound", "Document was not found."));
        }

        return Results.Ok(new
        {
            id = document.Id,
            fileName = document.OriginalFileName,
            sourceType = document.SourceType.ToString().ToLowerInvariant(),
            status = document.Status.ToString().ToLowerInvariant(),
            chunkCount = document.ChunkCount,
            errorMessage = document.ErrorMessage,
            createdAt = document.CreatedAt,
            indexedAt = document.IndexedAt
        });
    }

    private static async Task<IResult> DeleteDocumentAsync(
        Guid id,
        IDocumentRepository documentRepository,
        IChunkRepository chunkRepository,
        IManagedFileStorage managedFileStorage,
        CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(id, cancellationToken);
        if (document is null || document.DeletedAt is not null)
        {
            return Results.NoContent();
        }

        await chunkRepository.DeleteByDocumentIdAsync(id, cancellationToken);
        await documentRepository.SoftDeleteAsync(id, cancellationToken);
        await managedFileStorage.DeleteManagedFileAsync(document.StoredFileName, cancellationToken);

        return Results.NoContent();
    }

    private static ApiError? ValidateUpload(string fileName, long sizeBytes, AppOptions options)
    {
        var errors = new Dictionary<string, string>();

        if (sizeBytes <= 0 || sizeBytes > options.MaxUploadBytes)
        {
            errors["size"] = $"size must be greater than 0 and less than or equal to {options.MaxUploadBytes}.";
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return new ApiError("UnsupportedFileType", "Only text-bearing documents are supported. Images, videos, audio, archives, and OCR-only inputs are not supported in V1.");
        }

        return errors.Count == 0 ? null : new ApiError("ValidationError", "Request validation failed.", errors);
    }

    private static bool IsSymlinkOrReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) != 0;
    }

    private sealed record ImportFolderRequest(bool Recursive);
}
