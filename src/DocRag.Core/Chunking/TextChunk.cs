namespace DocRag.Core.Chunking;

public sealed record TextChunk(
    int ChunkIndex,
    string Content,
    int TokenCount,
    int? PageStart,
    int? PageEnd,
    string? Heading,
    IReadOnlyDictionary<string, object?> Metadata);
