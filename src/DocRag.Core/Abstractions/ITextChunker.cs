using DocRag.Core.Chunking;
using DocRag.Core.Documents;

namespace DocRag.Core.Abstractions;

public interface ITextChunker
{
    IReadOnlyList<TextChunk> Chunk(ExtractedDocumentText documentText, ChunkingOptions options);
}
