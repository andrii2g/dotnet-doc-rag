using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DocRag.Core.Abstractions;
using DocRag.Infrastructure.Database;
using DocRag.Infrastructure.Documents;
using DocRag.Infrastructure.Retrieval;
using Npgsql;

namespace DocRag.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocRagInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres must be configured.");
        }

        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<IManagedFileStorage, LocalManagedFileStorage>();
        services.AddScoped<IDocumentRepository, PostgresDocumentRepository>();
        services.AddScoped<IIngestionJobRepository, PostgresIngestionJobRepository>();
        services.AddScoped<IChunkRepository, PlaceholderChunkRepository>();

        return services;
    }
}
