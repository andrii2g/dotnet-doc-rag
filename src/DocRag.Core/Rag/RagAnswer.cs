namespace DocRag.Core.Rag;

public sealed record RagAnswer(
    string Answer,
    IReadOnlyList<RagCitation> Citations,
    IReadOnlyList<RagContextItem> Context);

public sealed record RagCitation(
    int SourceId,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? PageStart,
    int? PageEnd,
    string? Heading,
    double SimilarityScore);

public sealed record RagContextItem(
    int SourceId,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? PageStart,
    int? PageEnd,
    string? Heading,
    double SimilarityScore,
    string Content);
