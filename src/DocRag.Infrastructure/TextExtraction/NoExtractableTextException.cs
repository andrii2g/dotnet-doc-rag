namespace DocRag.Infrastructure.TextExtraction;

public sealed class NoExtractableTextException(string message) : Exception(message);
