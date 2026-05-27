using DocRag.Core.Abstractions;
using DocRag.Core.Documents;
using Npgsql;

namespace DocRag.Infrastructure.Documents;

public sealed class IngestionJobRepository(NpgsqlDataSource dataSource) : IIngestionJobRepository
{
    private readonly NpgsqlDataSource _dataSource = dataSource;

    public async Task CreateAsync(Guid documentId, CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO ingestion_jobs (
                id, document_id, status, attempt_count, max_attempts, created_at, updated_at
            )
            VALUES (
                @id, @document_id, 'queued', 0, 3, now(), now()
            )
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("document_id", documentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IngestionJobRecord?> ClaimNextAsync(string workerId, CancellationToken cancellationToken)
    {
        const string sql =
            """
            WITH next_job AS (
                SELECT id
                FROM ingestion_jobs
                WHERE status = 'queued'
                ORDER BY created_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE ingestion_jobs j
            SET status = 'processing',
                attempt_count = attempt_count + 1,
                locked_by = @worker_id,
                locked_at = now(),
                updated_at = now()
            FROM next_job
            WHERE j.id = next_job.id
            RETURNING j.id, j.document_id, j.status, j.attempt_count, j.max_attempts,
                      j.locked_by, j.locked_at, j.error_message, j.created_at, j.updated_at, j.completed_at;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worker_id", workerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var result = ReadJob(reader);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task MarkSucceededAsync(Guid jobId, CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE ingestion_jobs
            SET status = 'succeeded',
                updated_at = now(),
                completed_at = now(),
                locked_by = NULL,
                locked_at = NULL,
                error_message = NULL
            WHERE id = @id
            """;

        return ExecuteNonQueryAsync(sql, cancellationToken, ("id", jobId));
    }

    public Task MarkFailedAsync(Guid jobId, string errorMessage, bool retryable, CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE ingestion_jobs
            SET status = CASE
                    WHEN @retryable AND attempt_count < max_attempts THEN 'queued'
                    ELSE 'failed'
                END,
                updated_at = now(),
                completed_at = CASE
                    WHEN @retryable AND attempt_count < max_attempts THEN NULL
                    ELSE now()
                END,
                locked_by = NULL,
                locked_at = NULL,
                error_message = @error_message
            WHERE id = @id
            """;

        return ExecuteNonQueryAsync(
            sql,
            cancellationToken,
            ("id", jobId),
            ("retryable", retryable),
            ("error_message", errorMessage));
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

    private static IngestionJobRecord ReadJob(NpgsqlDataReader reader)
    {
        return new IngestionJobRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            ParseStatus(reader.GetString(2)),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10));
    }

    private static IngestionJobStatus ParseStatus(string value) => value switch
    {
        "queued" => IngestionJobStatus.Queued,
        "processing" => IngestionJobStatus.Processing,
        "succeeded" => IngestionJobStatus.Succeeded,
        "failed" => IngestionJobStatus.Failed,
        _ => throw new InvalidOperationException($"Unknown ingestion job status '{value}'.")
    };
}
