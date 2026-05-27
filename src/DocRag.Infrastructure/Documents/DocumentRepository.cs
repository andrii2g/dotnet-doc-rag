using DocRag.Core.Abstractions;
using DocRag.Core.Documents;
using Npgsql;

namespace DocRag.Infrastructure.Documents;

public sealed class DocumentRepository(NpgsqlDataSource dataSource) : IDocumentRepository
{
    private readonly NpgsqlDataSource _dataSource = dataSource;

    public async Task<DocumentRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id, original_file_name, stored_file_name, extension, content_type, source_type, source_path,
                   size_bytes, content_sha256, status, error_message, chunk_count, embedding_provider,
                   embedding_model, embedding_dimensions, created_at, updated_at, indexed_at, deleted_at
            FROM documents
            WHERE id = @id
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDocument(reader) : null;
    }

    public async Task<DocumentRecord?> GetActiveByHashAsync(string sha256, CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id, original_file_name, stored_file_name, extension, content_type, source_type, source_path,
                   size_bytes, content_sha256, status, error_message, chunk_count, embedding_provider,
                   embedding_model, embedding_dimensions, created_at, updated_at, indexed_at, deleted_at
            FROM documents
            WHERE content_sha256 = @sha256
              AND deleted_at IS NULL
            ORDER BY created_at DESC
            LIMIT 1
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("sha256", sha256);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDocument(reader) : null;
    }

    public async Task<IReadOnlyList<DocumentRecord>> ListAsync(int limit, int offset, CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id, original_file_name, stored_file_name, extension, content_type, source_type, source_path,
                   size_bytes, content_sha256, status, error_message, chunk_count, embedding_provider,
                   embedding_model, embedding_dimensions, created_at, updated_at, indexed_at, deleted_at
            FROM documents
            WHERE deleted_at IS NULL
            ORDER BY created_at DESC
            LIMIT @limit OFFSET @offset
            """;

        var results = new List<DocumentRecord>();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadDocument(reader));
        }

        return results;
    }

    public async Task<DocumentRecord> CreateQueuedAsync(CreateDocumentCommand command, CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO documents (
                id, original_file_name, stored_file_name, extension, content_type, source_type, source_path,
                size_bytes, content_sha256, status, chunk_count, created_at, updated_at
            )
            VALUES (
                @id, @original_file_name, @stored_file_name, @extension, @content_type, @source_type, @source_path,
                @size_bytes, @content_sha256, 'queued', 0, now(), now()
            )
            RETURNING id, original_file_name, stored_file_name, extension, content_type, source_type, source_path,
                      size_bytes, content_sha256, status, error_message, chunk_count, embedding_provider,
                      embedding_model, embedding_dimensions, created_at, updated_at, indexed_at, deleted_at
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var sqlCommand = new NpgsqlCommand(sql, connection);
        sqlCommand.Parameters.AddWithValue("id", command.Id);
        sqlCommand.Parameters.AddWithValue("original_file_name", command.OriginalFileName);
        sqlCommand.Parameters.AddWithValue("stored_file_name", command.StoredFileName);
        sqlCommand.Parameters.AddWithValue("extension", command.Extension);
        sqlCommand.Parameters.AddWithValue("content_type", (object?)command.ContentType ?? DBNull.Value);
        sqlCommand.Parameters.AddWithValue("source_type", ToDatabaseValue(command.SourceType));
        sqlCommand.Parameters.AddWithValue("source_path", (object?)command.SourcePath ?? DBNull.Value);
        sqlCommand.Parameters.AddWithValue("size_bytes", command.SizeBytes);
        sqlCommand.Parameters.AddWithValue("content_sha256", command.ContentSha256);

        await using var reader = await sqlCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Failed to create queued document.");
        }

        return ReadDocument(reader);
    }

    public Task MarkProcessingAsync(Guid id, CancellationToken cancellationToken)
    {
        return UpdateDocumentStatusAsync(
            id,
            "processing",
            cancellationToken,
            """
            UPDATE documents
            SET status = 'processing',
                error_message = NULL,
                updated_at = now()
            WHERE id = @id
            """);
    }

    public Task MarkIndexedAsync(Guid id, int chunkCount, CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE documents
            SET status = 'indexed',
                chunk_count = @chunk_count,
                indexed_at = now(),
                updated_at = now(),
                error_message = NULL
            WHERE id = @id
            """;

        return ExecuteNonQueryAsync(sql, cancellationToken, ("id", id), ("chunk_count", chunkCount));
    }

    public Task MarkFailedAsync(Guid id, string errorMessage, CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE documents
            SET status = 'failed',
                error_message = @error_message,
                updated_at = now()
            WHERE id = @id
            """;

        return ExecuteNonQueryAsync(sql, cancellationToken, ("id", id), ("error_message", errorMessage));
    }

    public Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE documents
            SET status = 'deleted',
                deleted_at = now(),
                updated_at = now()
            WHERE id = @id
              AND deleted_at IS NULL
            """;

        return ExecuteNonQueryAsync(sql, cancellationToken, ("id", id));
    }

    private Task UpdateDocumentStatusAsync(Guid id, string _, CancellationToken cancellationToken, string sql)
    {
        return ExecuteNonQueryAsync(sql, cancellationToken, ("id", id));
    }

    private async Task ExecuteNonQueryAsync(
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DocumentRecord ReadDocument(NpgsqlDataReader reader)
    {
        return new DocumentRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            ParseSourceType(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt64(7),
            reader.GetString(8),
            ParseDocumentStatus(reader.GetString(9)),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetInt32(14),
            reader.GetFieldValue<DateTimeOffset>(15),
            reader.GetFieldValue<DateTimeOffset>(16),
            reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17),
            reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18));
    }

    private static DocumentStatus ParseDocumentStatus(string value) => value switch
    {
        "queued" => DocumentStatus.Queued,
        "processing" => DocumentStatus.Processing,
        "indexed" => DocumentStatus.Indexed,
        "failed" => DocumentStatus.Failed,
        "deleted" => DocumentStatus.Deleted,
        _ => throw new InvalidOperationException($"Unknown document status '{value}'.")
    };

    private static DocumentSourceType ParseSourceType(string value) => value switch
    {
        "upload" => DocumentSourceType.Upload,
        "import" => DocumentSourceType.Import,
        _ => throw new InvalidOperationException($"Unknown document source type '{value}'.")
    };

    private static string ToDatabaseValue(DocumentSourceType value) => value switch
    {
        DocumentSourceType.Upload => "upload",
        DocumentSourceType.Import => "import",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}
