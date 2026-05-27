using DocRag.Core.Abstractions;
using DocRag.Core.Documents;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DocRag.Infrastructure.TextExtraction;

public sealed class DocxTextExtractor : ITextExtractor
{
    public bool CanExtract(string extension) => extension == ".docx";

    public Task<ExtractedDocumentText> ExtractAsync(string managedFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var document = WordprocessingDocument.Open(managedFilePath, false);
        var body = document.MainDocumentPart?.Document.Body
            ?? throw new InvalidOperationException("DOCX document body is missing.");

        var sections = new List<ExtractedTextSection>();
        string? currentHeading = null;

        foreach (var element in body.Elements())
        {
            switch (element)
            {
                case Paragraph paragraph:
                {
                    var text = PlainTextExtractor.NormalizeText(paragraph.InnerText);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    if (IsHeading(paragraph))
                    {
                        currentHeading = text;
                    }

                    sections.Add(new ExtractedTextSection(
                        text,
                        null,
                        currentHeading,
                        new Dictionary<string, string>()));
                    break;
                }
                case Table table:
                {
                    var rowTexts = table.Elements<TableRow>()
                        .Select(row => string.Join(" | ", row.Elements<TableCell>().Select(cell => PlainTextExtractor.NormalizeText(cell.InnerText))))
                        .Where(text => !string.IsNullOrWhiteSpace(text));

                    foreach (var rowText in rowTexts)
                    {
                        sections.Add(new ExtractedTextSection(
                            rowText,
                            null,
                            currentHeading,
                            new Dictionary<string, string>()));
                    }
                    break;
                }
            }
        }

        var fullText = string.Join("\n\n", sections.Select(section => section.Text));
        return Task.FromResult(new ExtractedDocumentText(fullText, sections, new Dictionary<string, string>()));
    }

    private static bool IsHeading(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        return !string.IsNullOrWhiteSpace(styleId) &&
               styleId.Contains("Heading", StringComparison.OrdinalIgnoreCase);
    }
}
