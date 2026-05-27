namespace DocRag.Core.Documents;

public sealed record ExtractedDocumentText(
    string Text,
    IReadOnlyList<ExtractedTextSection> Sections,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ExtractedTextSection(
    string Text,
    int? PageNumber,
    string? Heading,
    IReadOnlyDictionary<string, string> Metadata);
