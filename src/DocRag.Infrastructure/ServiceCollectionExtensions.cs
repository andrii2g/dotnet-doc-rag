using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DocRag.Core.Abstractions;
using DocRag.Infrastructure.Chunking;
using DocRag.Infrastructure.Database;
using DocRag.Infrastructure.Documents;
using DocRag.Infrastructure.Retrieval;
using DocRag.Infrastructure.TextExtraction;
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
        services.AddSingleton<IManagedFileStorage, LocalManagedFileStorage>();
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, DocxTextExtractor>();
        services.AddSingleton<ITextExtractor, HtmlTextExtractor>();
        services.AddSingleton<ITextExtractor, CsvTextExtractor>();
        services.AddSingleton<ITextExtractorResolver, TextExtractorResolver>();
        services.AddSingleton<ITextChunker, SimpleTextChunker>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IIngestionJobRepository, IngestionJobRepository>();
        services.AddScoped<IChunkRepository, PlaceholderChunkRepository>();

        return services;
    }
}
