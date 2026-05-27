using DocRag.Core.Chunking;
using DocRag.Core.Documents;
using DocRag.Infrastructure.Chunking;
using FluentAssertions;

namespace DocRag.UnitTests;

public sealed class SimpleTextChunkerTests
{
    [Fact]
    public void Chunk_ShouldCreateSingleChunk_ForShortText()
    {
        var chunker = new SimpleTextChunker();
        var document = new ExtractedDocumentText(
            "Short text",
            [new ExtractedTextSection("Short text", null, "Intro", new Dictionary<string, string>())],
            new Dictionary<string, string>());

        var chunks = chunker.Chunk(document, new ChunkingOptions(800, 120));

        chunks.Should().HaveCount(1);
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[0].Content.Should().Be("Short text");
    }

    [Fact]
    public void Chunk_ShouldNotCreateEmptyChunks()
    {
        var chunker = new SimpleTextChunker();
        var document = new ExtractedDocumentText(
            "   ",
            [new ExtractedTextSection("   ", null, null, new Dictionary<string, string>())],
            new Dictionary<string, string>());

        var chunks = chunker.Chunk(document, new ChunkingOptions(800, 120));

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void Chunk_ShouldKeepStableChunkIndexes()
    {
        var chunker = new SimpleTextChunker();
        var longText = new string('a', 5000);
        var document = new ExtractedDocumentText(
            longText,
            [new ExtractedTextSection(longText, 2, "Body", new Dictionary<string, string>())],
            new Dictionary<string, string>());

        var chunks = chunker.Chunk(document, new ChunkingOptions(100, 20));

        chunks.Select(chunk => chunk.ChunkIndex).Should().BeEquivalentTo(Enumerable.Range(0, chunks.Count));
        chunks.Should().OnlyContain(chunk => chunk.PageStart == 2 && chunk.PageEnd == 2);
    }
}
