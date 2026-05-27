using Npgsql;

namespace DocRag.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

        endpoints.MapGet("/health/ready", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            var extensionExists = await ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM pg_extension
                WHERE extname = 'vector'
                """,
                connection,
                cancellationToken) > 0;

            if (!extensionExists)
            {
                return Results.Problem("pgvector extension is not installed.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var requiredTablesExist = await ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name IN ('schema_migrations', 'documents', 'ingestion_jobs', 'document_chunks')
                """,
                connection,
                cancellationToken) == 4;

            if (!requiredTablesExist)
            {
                return Results.Problem("Required tables are missing.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new { status = "ready" });
        });

        return endpoints;
    }

    private static async Task<T> ExecuteScalarAsync<T>(string sql, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (T)Convert.ChangeType(result!, typeof(T));
    }
}
