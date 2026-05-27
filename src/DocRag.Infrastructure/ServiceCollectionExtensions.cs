using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DocRag.Infrastructure.Database;
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

        return services;
    }
}
