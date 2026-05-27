namespace DocRag.Core.Retrieval;

public sealed record RetrievalQuery(
    float[] QueryEmbedding,
    int CandidateK,
    int HnswEfSearch,
    IReadOnlyList<Guid>? DocumentIds);
