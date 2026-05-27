using Microsoft.Extensions.DependencyInjection;

namespace DocRag.Infrastructure.Database;

public static class MigrationServiceExtensions
{
    public static Task ApplyDocRagMigrationsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var migrator = serviceProvider.GetRequiredService<DatabaseMigrator>();
        return migrator.ApplyMigrationsAsync(cancellationToken);
    }
}
