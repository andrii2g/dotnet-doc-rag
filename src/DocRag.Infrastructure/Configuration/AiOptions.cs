using System.ComponentModel.DataAnnotations;

namespace DocRag.Infrastructure.Configuration;

public sealed class AiOptions
{
    [Required]
    public string Provider { get; init; } = "OpenAI";

    [Required]
    public string OpenAIApiKey { get; init; } = string.Empty;

    [Required]
    public string ChatModel { get; init; } = "gpt-5.5";
}
