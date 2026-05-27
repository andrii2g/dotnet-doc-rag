namespace DocRag.Api.Middleware;

public static class ApiKeyRequestPolicy
{
    public static bool AllowsAnonymous(PathString path, bool isDevelopment)
    {
        if (path.StartsWithSegments("/health/live") || path.StartsWithSegments("/health/ready"))
        {
            return true;
        }

        return isDevelopment &&
               (path.StartsWithSegments("/docs") || path.StartsWithSegments("/openapi"));
    }
}
