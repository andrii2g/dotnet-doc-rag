namespace DocRag.Api;

public sealed record ApiError(string Error, string Message, object? Details = null);
