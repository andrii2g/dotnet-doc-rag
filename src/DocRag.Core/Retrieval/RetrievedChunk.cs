namespace DocRag.Core.Retrieval;

public sealed record RetrievedChunk(
    Guid ChunkId,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    string Content,
    int? PageStart,
    int? PageEnd,
    string? Heading,
    double SimilarityScore);
