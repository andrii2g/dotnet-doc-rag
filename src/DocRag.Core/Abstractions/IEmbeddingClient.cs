namespace DocRag.Core.Abstractions;

public interface IEmbeddingClient
{
    Task<float[]> EmbedQueryAsync(string input, CancellationToken cancellationToken);
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);
}
