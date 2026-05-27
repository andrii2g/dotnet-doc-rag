using System.Text.Json;
using DocRag.Core.Abstractions;
using DocRag.Core.Embeddings;
using DocRag.Core.Retrieval;
using Npgsql;

namespace DocRag.Infrastructure.Retrieval;

public sealed class ChunkRepository(NpgsqlDataSource dataSource) : IChunkRepository
{
    private readonly NpgsqlDataSource _dataSource = dataSource;

    public async Task ReplaceChunksAsync(
        Guid documentId,
        IReadOnlyList<DocumentChunkToInsert> chunks,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await ExecuteAsync(
                """
                DELETE FROM document_chunks
                WHERE document_id = @document_id
                """,
                connection,
                transaction,
                cancellationToken,
                ("document_id", documentId));

            foreach (var chunk in chunks)
            {
                const string insertSql =
                    """
                    INSERT INTO document_chunks (
                        id, document_id, chunk_index, content, content_sha256, token_count,
                        page_start, page_end, heading, embedding, embedding_provider,
                        embedding_model, embedding_dimensions, metadata
                    )
                    VALUES (
                        @id, @document_id, @chunk_index, @content, @content_sha256, @token_count,
                        @page_start, @page_end, @heading, CAST(@embedding AS vector), @embedding_provider,
                        @embedding_model, @embedding_dimensions, CAST(@metadata AS jsonb)
                    )
                    """;

                await ExecuteAsync(
                    insertSql,
                    connection,
                    transaction,
                    cancellationToken,
                    ("id", chunk.Id),
                    ("document_id", chunk.DocumentId),
                    ("chunk_index", chunk.ChunkIndex),
                    ("content", chunk.Content),
                    ("content_sha256", chunk.ContentSha256),
                    ("token_count", chunk.TokenCount),
                    ("page_start", (object?)chunk.PageStart ?? DBNull.Value),
                    ("page_end", (object?)chunk.PageEnd ?? DBNull.Value),
                    ("heading", (object?)chunk.Heading ?? DBNull.Value),
                    ("embedding", ToVectorLiteral(chunk.Embedding)),
                    ("embedding_provider", chunk.EmbeddingProvider),
                    ("embedding_model", chunk.EmbeddingModel),
                    ("embedding_dimensions", chunk.EmbeddingDimensions),
                    ("metadata", JsonSerializer.Serialize(chunk.Metadata)));
            }

            await ExecuteAsync(
                """
                UPDATE documents
                SET status = 'indexed',
                    chunk_count = @chunk_count,
                    embedding_provider = @embedding_provider,
                    embedding_model = @embedding_model,
                    embedding_dimensions = @embedding_dimensions,
                    indexed_at = now(),
                    updated_at = now(),
                    error_message = NULL
                WHERE id = @document_id
                """,
                connection,
                transaction,
                cancellationToken,
                ("document_id", documentId),
                ("chunk_count", chunks.Count),
                ("embedding_provider", EmbeddingDefaults.Provider),
                ("embedding_model", EmbeddingDefaults.Model),
                ("embedding_dimensions", EmbeddingDefaults.Dimensions));

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(RetrievalQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            "SET LOCAL hnsw.ef_search = @ef_search",
            connection,
            transaction,
            cancellationToken,
            ("ef_search", query.HnswEfSearch));

        var sql =
            """
            SELECT
                c.id,
                c.document_id,
                d.original_file_name,
                c.chunk_index,
                c.content,
                c.page_start,
                c.page_end,
                c.heading,
                1 - (c.embedding <=> CAST(@query_embedding AS vector)) AS similarity_score
            FROM document_chunks c
            JOIN documents d ON d.id = c.document_id
            WHERE d.status = 'indexed'
              AND d.deleted_at IS NULL
              AND c.embedding_model = 'text-embedding-3-small'
              AND c.embedding_dimensions = 1536
            """;

        if (query.DocumentIds is { Count: > 0 })
        {
            sql += " AND c.document_id = ANY(@document_ids)";
        }

        sql += """

            ORDER BY c.embedding <=> CAST(@query_embedding AS vector)
            LIMIT @candidate_k
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("query_embedding", ToVectorLiteral(query.QueryEmbedding));
        command.Parameters.AddWithValue("candidate_k", query.CandidateK);

        if (query.DocumentIds is { Count: > 0 })
        {
            command.Parameters.AddWithValue("document_ids", query.DocumentIds.ToArray());
        }

        var results = new List<RetrievedChunk>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RetrievedChunk(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetDouble(8)));
        }

        await transaction.CommitAsync(cancellationToken);
        return results;
    }

    public Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            DELETE FROM document_chunks
            WHERE document_id = @document_id
            """,
            cancellationToken,
            ("document_id", documentId));
    }

    private Task ExecuteAsync(
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        return ExecuteAsync(sql, connection: null, transaction: null, cancellationToken, parameters);
    }

    private async Task ExecuteAsync(
        string sql,
        NpgsqlConnection? connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        var ownsConnection = connection is null;
        await using var localConnection = ownsConnection ? await _dataSource.OpenConnectionAsync(cancellationToken) : null;
        var activeConnection = connection ?? localConnection!;

        await using var command = new NpgsqlCommand(sql, activeConnection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ToVectorLiteral(IReadOnlyList<float> values)
    {
        return $"[{string.Join(",", values.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)))}]";
    }
}
