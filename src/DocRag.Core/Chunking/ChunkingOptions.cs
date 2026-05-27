namespace DocRag.Core.Chunking;

public sealed record ChunkingOptions(
    int ChunkTokenSize,
    int ChunkTokenOverlap);
