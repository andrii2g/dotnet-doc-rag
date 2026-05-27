using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DocRag.Infrastructure.Database;

public static class MigrationServiceExtensions
{
    public static Task ApplyDocRagMigrationsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
        if (environment.IsEnvironment("Testing"))
        {
            return Task.CompletedTask;
        }

        var migrator = serviceProvider.GetRequiredService<DatabaseMigrator>();
        return migrator.ApplyMigrationsAsync(cancellationToken);
    }
}
