namespace DocRag.Core.Documents;

public sealed record CreateDocumentCommand(
    Guid Id,
    string OriginalFileName,
    string StoredFileName,
    string Extension,
    string? ContentType,
    DocumentSourceType SourceType,
    string? SourcePath,
    long SizeBytes,
    string ContentSha256);
