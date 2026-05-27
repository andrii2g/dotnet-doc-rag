using DocRag.Core.Documents;

namespace DocRag.Core.Abstractions;

public interface ITextExtractor
{
    bool CanExtract(string extension);
    Task<ExtractedDocumentText> ExtractAsync(string managedFilePath, CancellationToken cancellationToken);
}
