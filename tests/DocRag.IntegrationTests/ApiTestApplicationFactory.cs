using DocRag.Core.Abstractions;
using DocRag.Core.Documents;
using DocRag.Core.Retrieval;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocRag.IntegrationTests;

public sealed class ApiTestApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<Guid, DocumentRecord> _documents = [];

    public RecordingChunkRepository ChunkRepository { get; } = new();
    public RecordingManagedFileStorage ManagedFileStorage { get; } = new();

    public void SeedDocument(DocumentRecord document)
    {
        _documents[document.Id] = document;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(configurationBuilder =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=doc_rag;Username=postgres;Password=postgres",
                ["App:StoragePath"] = "storage",
                ["App:ImportPath"] = "samples",
                ["App:MaxUploadBytes"] = "10485760",
                ["App:AllowedExtensions:0"] = ".txt",
                ["App:AllowedExtensions:1"] = ".md",
                ["App:AllowedExtensions:2"] = ".pdf",
                ["App:AllowedExtensions:3"] = ".docx",
                ["App:AllowedExtensions:4"] = ".html",
                ["App:AllowedExtensions:5"] = ".csv",
                ["Rag:DefaultTopK"] = "5",
                ["Rag:MaxTopK"] = "20",
                ["Rag:DefaultCandidateK"] = "20",
                ["Rag:MaxCandidateK"] = "100",
                ["Rag:HnswEfSearch"] = "100",
                ["Rag:MinSimilarity"] = "0.2",
                ["AI:Provider"] = "OpenAI",
                ["AI:OpenAIApiKey"] = "test-openai-key",
                ["AI:ChatModel"] = "gpt-4.1-mini",
                ["Security:ApiKey"] = "test-api-key"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDocumentRepository>();
            services.RemoveAll<IChunkRepository>();
            services.RemoveAll<IManagedFileStorage>();

            services.AddSingleton<IDocumentRepository>(new InMemoryDocumentRepository(_documents));
            services.AddSingleton<IChunkRepository>(ChunkRepository);
            services.AddSingleton<IManagedFileStorage>(ManagedFileStorage);
        });
    }

    public sealed class InMemoryDocumentRepository(Dictionary<Guid, DocumentRecord> documents) : IDocumentRepository
    {
        private readonly Dictionary<Guid, DocumentRecord> _documents = documents;

        public Task<DocumentRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            _documents.TryGetValue(id, out var document);
            return Task.FromResult(document);
        }

        public Task<DocumentRecord?> GetActiveByHashAsync(string sha256, CancellationToken cancellationToken)
        {
            var document = _documents.Values.FirstOrDefault(x =>
                x.ContentSha256 == sha256 &&
                x.DeletedAt is null);

            return Task.FromResult(document);
        }

        public Task<IReadOnlyList<DocumentRecord>> ListAsync(int limit, int offset, CancellationToken cancellationToken)
        {
            IReadOnlyList<DocumentRecord> items = _documents.Values
                .OrderByDescending(x => x.CreatedAt)
                .Skip(offset)
                .Take(limit)
                .ToArray();

            return Task.FromResult(items);
        }

        public Task<DocumentRecord> CreateQueuedAsync(CreateDocumentCommand command, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var document = new DocumentRecord(
                command.Id,
                command.OriginalFileName,
                command.StoredFileName,
                command.Extension,
                command.ContentType,
                command.SourceType,
                command.SourcePath,
                command.SizeBytes,
                command.ContentSha256,
                DocumentStatus.Queued,
                null,
                0,
                null,
                null,
                null,
                now,
                now,
                null,
                null);

            _documents[document.Id] = document;
            return Task.FromResult(document);
        }

        public Task MarkProcessingAsync(Guid id, CancellationToken cancellationToken)
        {
            Update(id, document => document with
            {
                Status = DocumentStatus.Processing,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            return Task.CompletedTask;
        }

        public Task MarkIndexedAsync(Guid id, int chunkCount, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            Update(id, document => document with
            {
                Status = DocumentStatus.Indexed,
                ChunkCount = chunkCount,
                UpdatedAt = now,
                IndexedAt = now
            });

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid id, string errorMessage, CancellationToken cancellationToken)
        {
            Update(id, document => document with
            {
                Status = DocumentStatus.Failed,
                ErrorMessage = errorMessage,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            return Task.CompletedTask;
        }

        public Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            Update(id, document => document with
            {
                DeletedAt = now,
                UpdatedAt = now
            });

            return Task.CompletedTask;
        }

        private void Update(Guid id, Func<DocumentRecord, DocumentRecord> update)
        {
            if (_documents.TryGetValue(id, out var existing))
            {
                _documents[id] = update(existing);
            }
        }
    }

    public sealed class RecordingChunkRepository : IChunkRepository
    {
        public List<Guid> DeletedDocumentIds { get; } = [];

        public Task ReplaceChunksAsync(Guid documentId, IReadOnlyList<DocumentChunkToInsert> chunks, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(RetrievalQuery query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RetrievedChunk>>([]);

        public Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken)
        {
            DeletedDocumentIds.Add(documentId);
            return Task.CompletedTask;
        }
    }

    public sealed class RecordingManagedFileStorage : IManagedFileStorage
    {
        public List<string> DeletedStoredFileNames { get; } = [];

        public Task<StoredFileResult> StoreUploadAsync(Stream source, string originalFileName, string? contentType, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<StoredFileResult> StoreImportFileAsync(string sourcePath, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public string CreateStoredFileName(Guid documentId, string extension)
            => $"{documentId:N}{extension}";

        public Task PromoteTempFileAsync(string tempFilePath, string storedFileName, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DeleteManagedFileAsync(string storedFileName, CancellationToken cancellationToken)
        {
            DeletedStoredFileNames.Add(storedFileName);
            return Task.CompletedTask;
        }

        public string GetManagedPath(string storedFileName)
            => Path.Combine("storage", storedFileName);
    }
}
