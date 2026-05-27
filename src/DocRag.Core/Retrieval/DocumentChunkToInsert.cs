namespace DocRag.Core.Retrieval;

public sealed record DocumentChunkToInsert(
    Guid Id,
    Guid DocumentId,
    int ChunkIndex,
    string Content,
    string ContentSha256,
    int TokenCount,
    int? PageStart,
    int? PageEnd,
    string? Heading,
    float[] Embedding,
    string EmbeddingProvider,
    string EmbeddingModel,
    int EmbeddingDimensions,
    IReadOnlyDictionary<string, object?> Metadata);
