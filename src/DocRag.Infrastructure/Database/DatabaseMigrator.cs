using System.Reflection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DocRag.Infrastructure.Database;

public sealed class DatabaseMigrator(NpgsqlDataSource dataSource, ILogger<DatabaseMigrator> logger)
{
    private const string MigrationResourceMarker = ".Migrations.";
    private const string MigrationResourceSuffix = ".sql";
    private const string LockName = "dotnet-doc-rag-migrations";

    private readonly NpgsqlDataSource _dataSource = dataSource;
    private readonly ILogger<DatabaseMigrator> _logger = logger;
    private readonly Assembly _assembly = typeof(DatabaseMigrator).Assembly;

    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        var migrations = GetMigrations();

        if (migrations.Count == 0)
        {
            _logger.LogInformation("No embedded SQL migrations were found.");
            return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await AcquireAdvisoryLockAsync(connection, transaction, cancellationToken);
            await EnsureSchemaMigrationsTableAsync(connection, transaction, cancellationToken);

            var appliedMigrations = await GetAppliedMigrationNamesAsync(connection, transaction, cancellationToken);

            foreach (var migration in migrations)
            {
                if (appliedMigrations.Contains(migration.Name))
                {
                    continue;
                }

                _logger.LogInformation("Applying migration {MigrationName}", migration.Name);

                await using var applyCommand = new NpgsqlCommand(migration.Sql, connection, transaction);
                await applyCommand.ExecuteNonQueryAsync(cancellationToken);

                await using var recordCommand = new NpgsqlCommand(
                    """
                    INSERT INTO schema_migrations (name)
                    VALUES (@name)
                    """,
                    connection,
                    transaction);

                recordCommand.Parameters.AddWithValue("name", migration.Name);
                await recordCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            await ReleaseAdvisoryLockAsync(connection, cancellationToken);
        }
    }

    private IReadOnlyList<EmbeddedMigration> GetMigrations()
    {
        return _assembly
            .GetManifestResourceNames()
            .Where(name => name.Contains(MigrationResourceMarker, StringComparison.Ordinal) &&
                           name.EndsWith(MigrationResourceSuffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new EmbeddedMigration(GetMigrationName(name), ReadMigrationSql(name)))
            .ToArray();
    }

    private string ReadMigrationSql(string resourceName)
    {
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string GetMigrationName(string resourceName)
    {
        var markerIndex = resourceName.IndexOf(MigrationResourceMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return resourceName;
        }

        return resourceName[(markerIndex + MigrationResourceMarker.Length)..];
    }

    private static async Task AcquireAdvisoryLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_lock(hashtext(@lock_name));",
            connection,
            transaction);

        command.Parameters.AddWithValue("lock_name", LockName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReleaseAdvisoryLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_unlock(hashtext(@lock_name));",
            connection);

        command.Parameters.AddWithValue("lock_name", LockName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureSchemaMigrationsTableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                name text PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now()
            );
            """,
            connection,
            transaction);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> GetAppliedMigrationNamesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT name
            FROM schema_migrations
            ORDER BY name
            """,
            connection,
            transaction);

        var appliedMigrations = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            appliedMigrations.Add(reader.GetString(0));
        }

        return appliedMigrations;
    }

    private sealed record EmbeddedMigration(string Name, string Sql);
}
