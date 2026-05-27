using DocRag.Core.Abstractions;
using DocRag.Core.Documents;
using DocRag.Core.Rag;
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
    private readonly string _importRoot = Path.Combine(Path.GetTempPath(), "dotnet-doc-rag-tests", Guid.NewGuid().ToString("N"), "import");

    public RecordingChunkRepository ChunkRepository { get; } = new();
    public RecordingManagedFileStorage ManagedFileStorage { get; } = new();
    public RecordingIngestionJobRepository IngestionJobRepository { get; } = new();
    public RecordingEmbeddingClient EmbeddingClient { get; } = new();
    public RecordingRagAnswerGenerator RagAnswerGenerator { get; } = new();
    public string ImportRoot => _importRoot;

    public void SeedDocument(DocumentRecord document)
    {
        _documents[document.Id] = document;
    }

    public void Reset()
    {
        _documents.Clear();
        ChunkRepository.Reset();
        ManagedFileStorage.Reset();
        IngestionJobRepository.Reset();
        EmbeddingClient.Reset();
        RagAnswerGenerator.Reset();

        if (Directory.Exists(_importRoot))
        {
            Directory.Delete(_importRoot, recursive: true);
        }

        Directory.CreateDirectory(_importRoot);
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
                ["App:ImportPath"] = _importRoot,
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
            services.RemoveAll<IEmbeddingClient>();
            services.RemoveAll<IIngestionJobRepository>();
            services.RemoveAll<IManagedFileStorage>();
            services.RemoveAll<IRagAnswerGenerator>();

            services.AddSingleton<IDocumentRepository>(new InMemoryDocumentRepository(_documents));
            services.AddSingleton<IChunkRepository>(ChunkRepository);
            services.AddSingleton<IEmbeddingClient>(EmbeddingClient);
            services.AddSingleton<IIngestionJobRepository>(IngestionJobRepository);
            services.AddSingleton<IManagedFileStorage>(ManagedFileStorage);
            services.AddSingleton<IRagAnswerGenerator>(RagAnswerGenerator);
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
                .Where(x => x.DeletedAt is null)
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
        public IReadOnlyList<RetrievedChunk> SearchResults { get; set; } = [];
        public RetrievalQuery? LastQuery { get; private set; }

        public void Reset()
        {
            DeletedDocumentIds.Clear();
            SearchResults = [];
            LastQuery = null;
        }

        public Task ReplaceChunksAsync(Guid documentId, IReadOnlyList<DocumentChunkToInsert> chunks, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(RetrievalQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(SearchResults);
        }

        public Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken)
        {
            DeletedDocumentIds.Add(documentId);
            return Task.CompletedTask;
        }
    }

    public sealed class RecordingIngestionJobRepository : IIngestionJobRepository
    {
        public List<Guid> CreatedDocumentIds { get; } = [];

        public void Reset()
        {
            CreatedDocumentIds.Clear();
        }

        public Task CreateAsync(Guid documentId, CancellationToken cancellationToken)
        {
            CreatedDocumentIds.Add(documentId);
            return Task.CompletedTask;
        }

        public Task<IngestionJobRecord?> ClaimNextAsync(string workerId, CancellationToken cancellationToken)
            => Task.FromResult<IngestionJobRecord?>(null);

        public Task MarkSucceededAsync(Guid jobId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task MarkFailedAsync(Guid jobId, string errorMessage, bool retryable, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public sealed class RecordingEmbeddingClient : IEmbeddingClient
    {
        public List<string> Queries { get; } = [];
        public float[] QueryEmbedding { get; set; } = [0.1f, 0.2f, 0.3f];

        public void Reset()
        {
            Queries.Clear();
            QueryEmbedding = [0.1f, 0.2f, 0.3f];
        }

        public Task<float[]> EmbedQueryAsync(string input, CancellationToken cancellationToken)
        {
            Queries.Add(input);
            return Task.FromResult(QueryEmbedding);
        }

        public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<float[]>>(inputs.Select(_ => QueryEmbedding).ToArray());
    }

    public sealed class RecordingRagAnswerGenerator : IRagAnswerGenerator
    {
        public RagAnswer Response { get; set; } = new("I don't know.", [], []);
        public RagAnswerRequest? LastRequest { get; private set; }

        public void Reset()
        {
            Response = new RagAnswer("I don't know.", [], []);
            LastRequest = null;
        }

        public Task<RagAnswer> GenerateAsync(RagAnswerRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Response);
        }
    }

    public sealed class RecordingManagedFileStorage : IManagedFileStorage
    {
        public List<string> DeletedStoredFileNames { get; } = [];
        public List<string> PromotedStoredFileNames { get; } = [];

        public void Reset()
        {
            DeletedStoredFileNames.Clear();
            PromotedStoredFileNames.Clear();
        }

        public async Task<StoredFileResult> StoreUploadAsync(Stream source, string originalFileName, string? contentType, CancellationToken cancellationToken)
        {
            var tempFilePath = Path.GetTempFileName();
            await using var output = File.Create(tempFilePath);
            await source.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Close();

            return await CreateStoredFileResultAsync(tempFilePath, originalFileName, contentType, sourcePath: null, cancellationToken);
        }

        public async Task<StoredFileResult> StoreImportFileAsync(string sourcePath, CancellationToken cancellationToken)
        {
            var tempFilePath = Path.GetTempFileName();
            await using var source = File.OpenRead(sourcePath);
            await using var output = File.Create(tempFilePath);
            await source.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Close();

            return await CreateStoredFileResultAsync(tempFilePath, Path.GetFileName(sourcePath), GetContentType(sourcePath), sourcePath, cancellationToken);
        }

        public string CreateStoredFileName(Guid documentId, string extension)
            => $"{documentId:N}{extension}";

        public Task PromoteTempFileAsync(string tempFilePath, string storedFileName, CancellationToken cancellationToken)
        {
            PromotedStoredFileNames.Add(storedFileName);

            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }

            return Task.CompletedTask;
        }

        public Task DeleteManagedFileAsync(string storedFileName, CancellationToken cancellationToken)
        {
            DeletedStoredFileNames.Add(storedFileName);
            return Task.CompletedTask;
        }

        public string GetManagedPath(string storedFileName)
            => Path.Combine("storage", storedFileName);

        private static async Task<StoredFileResult> CreateStoredFileResultAsync(
            string tempFilePath,
            string originalFileName,
            string? contentType,
            string? sourcePath,
            CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(tempFilePath);
            await using var stream = File.OpenRead(tempFilePath);
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hashBytes = await sha.ComputeHashAsync(stream, cancellationToken);
            var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            return new StoredFileResult(
                originalFileName,
                tempFilePath,
                Path.GetExtension(originalFileName).ToLowerInvariant(),
                contentType,
                fileInfo.Length,
                hash,
                sourcePath);
        }

        private static string GetContentType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                ".html" => "text/html",
                ".csv" => "text/csv",
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                _ => "application/octet-stream"
            };
        }
    }
}
