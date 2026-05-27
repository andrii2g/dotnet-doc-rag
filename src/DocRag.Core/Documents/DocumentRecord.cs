namespace DocRag.Core.Documents;

public sealed record DocumentRecord(
    Guid Id,
    string OriginalFileName,
    string StoredFileName,
    string Extension,
    string? ContentType,
    DocumentSourceType SourceType,
    string? SourcePath,
    long SizeBytes,
    string ContentSha256,
    DocumentStatus Status,
    string? ErrorMessage,
    int ChunkCount,
    string? EmbeddingProvider,
    string? EmbeddingModel,
    int? EmbeddingDimensions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? IndexedAt,
    DateTimeOffset? DeletedAt);
