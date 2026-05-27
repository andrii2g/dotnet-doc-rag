using DocRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocRag.Api.Middleware;

public static class ApiKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseDocRagApiKey(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var securityOptions = context.RequestServices.GetRequiredService<IOptions<SecurityOptions>>().Value;
            var apiKey = securityOptions.ApiKey;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                await next();
                return;
            }

            var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();

            if (ApiKeyRequestPolicy.AllowsAnonymous(context.Request.Path, environment.IsDevelopment()))
            {
                await next();
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-Api-Key", out var providedKey) ||
                !StringValuesEqual(providedKey.ToString(), apiKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new ApiError("Unauthorized", "Missing or invalid API key."),
                    cancellationToken: context.RequestAborted);
                return;
            }

            await next();
        });
    }

    private static bool StringValuesEqual(string left, string right)
        => string.Equals(left, right, StringComparison.Ordinal);
}
