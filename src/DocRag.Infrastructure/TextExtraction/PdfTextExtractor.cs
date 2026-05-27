using DocRag.Core.Abstractions;
using DocRag.Core.Documents;
using UglyToad.PdfPig;

namespace DocRag.Infrastructure.TextExtraction;

public sealed class PdfTextExtractor : ITextExtractor
{
    public bool CanExtract(string extension) => extension == ".pdf";

    public Task<ExtractedDocumentText> ExtractAsync(string managedFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var document = PdfDocument.Open(managedFilePath);
        var sections = new List<ExtractedTextSection>();

        foreach (var page in document.GetPages())
        {
            var pageText = PlainTextExtractor.NormalizeText(page.Text);
            if (string.IsNullOrWhiteSpace(pageText))
            {
                continue;
            }

            sections.Add(new ExtractedTextSection(
                pageText,
                page.Number,
                null,
                new Dictionary<string, string>()));
        }

        if (sections.Count == 0)
        {
            throw new NoExtractableTextException("No extractable text found. OCR is not supported in V1.");
        }

        var fullText = string.Join("\n\n", sections.Select(section => section.Text));
        return Task.FromResult(new ExtractedDocumentText(fullText, sections, new Dictionary<string, string>()));
    }
}
