namespace DocRag.Core.Abstractions;

public interface ITextExtractorResolver
{
    ITextExtractor Resolve(string extension);
}
