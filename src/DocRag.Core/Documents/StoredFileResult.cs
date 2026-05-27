namespace DocRag.Core.Documents;

public sealed record StoredFileResult(
    string OriginalFileName,
    string TempFilePath,
    string Extension,
    string? ContentType,
    long SizeBytes,
    string ContentSha256,
    string? SourcePath);
