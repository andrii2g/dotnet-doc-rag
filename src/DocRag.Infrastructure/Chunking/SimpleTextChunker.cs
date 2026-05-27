using DocRag.Core.Abstractions;
using DocRag.Core.Chunking;
using DocRag.Core.Documents;

namespace DocRag.Infrastructure.Chunking;

public sealed class SimpleTextChunker : ITextChunker
{
    public IReadOnlyList<TextChunk> Chunk(ExtractedDocumentText documentText, ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(documentText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ChunkTokenSize);
        ArgumentOutOfRangeException.ThrowIfNegative(options.ChunkTokenOverlap);

        var sections = documentText.Sections
            .Where(section => !string.IsNullOrWhiteSpace(section.Text))
            .ToArray();

        if (sections.Length == 0)
        {
            return [];
        }

        var chunks = new List<TextChunk>();
        var chunkIndex = 0;

        foreach (var section in sections)
        {
            var normalizedText = NormalizeWhitespace(section.Text);
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                continue;
            }

            if (EstimateTokens(normalizedText) <= options.ChunkTokenSize)
            {
                chunks.Add(new TextChunk(
                    chunkIndex++,
                    normalizedText,
                    EstimateTokens(normalizedText),
                    section.PageNumber,
                    section.PageNumber,
                    section.Heading,
                    new Dictionary<string, object?>()));
                continue;
            }

            var stepCharacters = Math.Max(1, (options.ChunkTokenSize - options.ChunkTokenOverlap) * 4);
            var chunkCharacters = options.ChunkTokenSize * 4;

            for (var start = 0; start < normalizedText.Length; start += stepCharacters)
            {
                var length = Math.Min(chunkCharacters, normalizedText.Length - start);
                var piece = normalizedText.Substring(start, length).Trim();
                if (string.IsNullOrWhiteSpace(piece))
                {
                    continue;
                }

                chunks.Add(new TextChunk(
                    chunkIndex++,
                    piece,
                    EstimateTokens(piece),
                    section.PageNumber,
                    section.PageNumber,
                    section.Heading,
                    new Dictionary<string, object?>()));

                if (start + length >= normalizedText.Length)
                {
                    break;
                }
            }
        }

        return chunks;
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(
            "\n\n",
            value.Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(paragraph => string.Join(" ", paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
    }

    private static int EstimateTokens(string content) => (int)Math.Ceiling(content.Length / 4.0);
}
