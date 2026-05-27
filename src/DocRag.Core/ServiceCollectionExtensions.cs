using Microsoft.Extensions.DependencyInjection;

namespace DocRag.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocRagCore(this IServiceCollection services)
    {
        return services;
    }
}
