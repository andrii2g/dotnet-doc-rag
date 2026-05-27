using System.Security.Cryptography;
using DocRag.Core.Abstractions;
using DocRag.Core.Chunking;
using DocRag.Core.Documents;
using DocRag.Core.Embeddings;
using DocRag.Core.Retrieval;
using DocRag.Infrastructure.Configuration;
using DocRag.Infrastructure.Documents;
using DocRag.Infrastructure.TextExtraction;
using Microsoft.Extensions.Options;

namespace DocRag.Worker;

public sealed class IngestionWorker(
    ILogger<IngestionWorker> logger,
    IDocumentRepository documentRepository,
    IIngestionJobRepository ingestionJobRepository,
    IChunkRepository chunkRepository,
    IManagedFileStorage managedFileStorage,
    ITextExtractorResolver textExtractorResolver,
    ITextChunker textChunker,
    IEmbeddingClient embeddingClient,
    IOptions<RagOptions> ragOptions) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

    private readonly ILogger<IngestionWorker> _logger = logger;
    private readonly IDocumentRepository _documentRepository = documentRepository;
    private readonly IIngestionJobRepository _ingestionJobRepository = ingestionJobRepository;
    private readonly IChunkRepository _chunkRepository = chunkRepository;
    private readonly IManagedFileStorage _managedFileStorage = managedFileStorage;
    private readonly ITextExtractorResolver _textExtractorResolver = textExtractorResolver;
    private readonly ITextChunker _textChunker = textChunker;
    private readonly IEmbeddingClient _embeddingClient = embeddingClient;
    private readonly RagOptions _ragOptions = ragOptions.Value;
    private readonly string _workerId = $"worker-{Environment.MachineName}-{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _ingestionJobRepository.ClaimNextAsync(_workerId, stoppingToken);
            if (job is null)
            {
                await Task.Delay(IdleDelay, stoppingToken);
                continue;
            }

            try
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                var retryable = IsRetryable(ex) && job.AttemptCount < job.MaxAttempts;

                _logger.LogError(
                    ex,
                    "Ingestion job {JobId} for document {DocumentId} failed. Retryable: {Retryable}",
                    job.Id,
                    job.DocumentId,
                    retryable);

                await _ingestionJobRepository.MarkFailedAsync(job.Id, ex.Message, retryable, stoppingToken);

                if (!retryable)
                {
                    await _documentRepository.MarkFailedAsync(job.DocumentId, ex.Message, stoppingToken);
                }
            }
        }
    }

    private async Task ProcessJobAsync(IngestionJobRecord job, CancellationToken cancellationToken)
    {
        var document = await _documentRepository.GetByIdAsync(job.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException($"Document '{job.DocumentId}' was not found.");

        await _documentRepository.MarkProcessingAsync(document.Id, cancellationToken);

        var managedFilePath = _managedFileStorage.GetManagedPath(document.StoredFileName);
        var extractor = _textExtractorResolver.Resolve(document.Extension.ToLowerInvariant());
        var extractedText = await extractor.ExtractAsync(managedFilePath, cancellationToken);

        var chunks = _textChunker.Chunk(
            extractedText,
            new ChunkingOptions(_ragOptions.ChunkTokenSize, _ragOptions.ChunkTokenOverlap));

        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("Document extraction produced no chunkable content.");
        }

        var embeddings = await _embeddingClient.EmbedDocumentsAsync(
            chunks.Select(chunk => chunk.Content).ToArray(),
            cancellationToken);

        var chunkRows = chunks.Zip(embeddings, (chunk, embedding) => new DocumentChunkToInsert(
            Guid.NewGuid(),
            document.Id,
            chunk.ChunkIndex,
            chunk.Content,
            ComputeSha256(chunk.Content),
            chunk.TokenCount,
            chunk.PageStart,
            chunk.PageEnd,
            chunk.Heading,
            embedding,
            EmbeddingDefaults.Provider,
            EmbeddingDefaults.Model,
            EmbeddingDefaults.Dimensions,
            chunk.Metadata)).ToArray();

        await _chunkRepository.ReplaceChunksAsync(document.Id, chunkRows, cancellationToken);
        await _ingestionJobRepository.MarkSucceededAsync(job.Id, cancellationToken);

        _logger.LogInformation(
            "Indexed document {DocumentId} with {ChunkCount} chunks using {EmbeddingModel}/{Dimensions}.",
            document.Id,
            chunkRows.Length,
            EmbeddingDefaults.Model,
            EmbeddingDefaults.Dimensions);
    }

    private static string ComputeSha256(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsRetryable(Exception exception) => exception switch
    {
        NoExtractableTextException => false,
        NotSupportedException => false,
        InvalidOperationException invalidOperationException
            when invalidOperationException.Message.Contains("Embedding dimension mismatch", StringComparison.Ordinal) => false,
        _ => true
    };
}
