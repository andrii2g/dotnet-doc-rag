using DocRag.Core.Retrieval;

namespace DocRag.Core.Rag;

public sealed record RagAnswerRequest(
    string Question,
    IReadOnlyList<RetrievedChunk> RetrievedChunks,
    bool IncludeContext);
