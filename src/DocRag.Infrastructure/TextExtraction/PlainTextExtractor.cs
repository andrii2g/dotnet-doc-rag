using System.Text;
using System.Text.RegularExpressions;
using DocRag.Core.Abstractions;
using DocRag.Core.Documents;

namespace DocRag.Infrastructure.TextExtraction;

public sealed partial class PlainTextExtractor : ITextExtractor
{
    private static readonly HashSet<string> SupportedExtensions = [".txt", ".md"];

    public bool CanExtract(string extension) => SupportedExtensions.Contains(extension);

    public async Task<ExtractedDocumentText> ExtractAsync(string managedFilePath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(managedFilePath, Encoding.UTF8, cancellationToken);
        var normalized = NormalizeText(text);

        return new ExtractedDocumentText(
            normalized,
            [new ExtractedTextSection(normalized, null, null, new Dictionary<string, string>())],
            new Dictionary<string, string>());
    }

    public static string NormalizeText(string value)
    {
        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = InvalidControlCharactersRegex().Replace(normalized, string.Empty);
        return normalized.TrimEnd();
    }

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F]")]
    private static partial Regex InvalidControlCharactersRegex();
}
