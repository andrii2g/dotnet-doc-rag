using System.Globalization;
using CsvHelper;
using DocRag.Core.Abstractions;
using DocRag.Core.Documents;

namespace DocRag.Infrastructure.TextExtraction;

public sealed class CsvTextExtractor : ITextExtractor
{
    public bool CanExtract(string extension) => extension == ".csv";

    public async Task<ExtractedDocumentText> ExtractAsync(string managedFilePath, CancellationToken cancellationToken)
    {
        var sections = new List<ExtractedTextSection>();

        await using var stream = File.OpenRead(managedFilePath);
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];

        var rowNumber = 0;
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            var parts = headers.Select(header => $"{header}={csv.GetField(header)}");
            var rowText = $"row {rowNumber}: {string.Join("; ", parts)}";

            sections.Add(new ExtractedTextSection(
                rowText,
                null,
                null,
                new Dictionary<string, string>()));
        }

        var fullText = string.Join("\n", sections.Select(section => section.Text));
        return new ExtractedDocumentText(fullText, sections, new Dictionary<string, string>());
    }
}
