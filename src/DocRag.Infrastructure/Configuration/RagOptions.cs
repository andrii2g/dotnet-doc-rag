using System.ComponentModel.DataAnnotations;

namespace DocRag.Infrastructure.Configuration;

public sealed class RagOptions
{
    [Range(1, int.MaxValue)]
    public int ChunkTokenSize { get; init; } = 800;

    [Range(0, int.MaxValue)]
    public int ChunkTokenOverlap { get; init; } = 120;

    [Range(1, int.MaxValue)]
    public int DefaultTopK { get; init; } = 8;

    [Range(1, int.MaxValue)]
    public int MaxTopK { get; init; } = 20;

    [Range(1, int.MaxValue)]
    public int DefaultCandidateK { get; init; } = 24;

    [Range(1, int.MaxValue)]
    public int MaxCandidateK { get; init; } = 100;

    [Range(0d, 1d)]
    public double MinSimilarity { get; init; } = 0.20;

    [Range(1, int.MaxValue)]
    public int HnswEfSearch { get; init; } = 100;

    [Range(1, int.MaxValue)]
    public int MaxContextTokens { get; init; } = 6000;

    [Range(0d, 2d)]
    public float Temperature { get; init; } = 0.1f;
}
