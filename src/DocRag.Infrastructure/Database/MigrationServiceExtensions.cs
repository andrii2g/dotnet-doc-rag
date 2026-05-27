using Microsoft.Extensions.DependencyInjection;

namespace DocRag.Infrastructure.Database;

public static class MigrationServiceExtensions
{
    public static Task ApplyDocRagMigrationsAsync(this IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return Task.CompletedTask;
    }
}
