using DocRag.Core.Abstractions;

namespace DocRag.Infrastructure.TextExtraction;

public sealed class TextExtractorResolver(IEnumerable<ITextExtractor> extractors) : ITextExtractorResolver
{
    private readonly ITextExtractor[] _extractors = extractors.ToArray();

    public ITextExtractor Resolve(string extension)
    {
        var normalizedExtension = extension?.Trim().ToLowerInvariant() ?? string.Empty;
        var extractor = _extractors.FirstOrDefault(candidate => candidate.CanExtract(normalizedExtension));

        return extractor ?? throw new NotSupportedException($"No extractor is registered for '{normalizedExtension}'.");
    }
}
