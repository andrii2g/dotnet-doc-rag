using System.ComponentModel.DataAnnotations;

namespace DocRag.Infrastructure.Configuration;

public sealed class AppOptions
{
    [Required]
    public string StoragePath { get; init; } = "/app/storage/documents";

    [Required]
    public string ImportPath { get; init; } = "/app/import";

    [Range(1, long.MaxValue)]
    public long MaxUploadBytes { get; init; } = 25 * 1024 * 1024;

    [Required]
    [MinLength(1)]
    public string[] AllowedExtensions { get; init; } =
    [
        ".txt", ".md", ".pdf", ".docx", ".html", ".htm", ".csv"
    ];
}
