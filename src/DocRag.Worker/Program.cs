using DocRag.Core;
using DocRag.Infrastructure;
using DocRag.Infrastructure.Configuration;
using DocRag.Infrastructure.Database;
using DocRag.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<AppOptions>()
    .BindConfiguration("App")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<RagOptions>()
    .BindConfiguration("Rag")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AiOptions>()
    .BindConfiguration("AI")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SecurityOptions>()
    .BindConfiguration("Security")
    .ValidateOnStart();

builder.Services.AddDocRagCore();
builder.Services.AddDocRagInfrastructure(builder.Configuration);
builder.Services.AddHostedService<IngestionWorker>();

var host = builder.Build();

await host.Services.ApplyDocRagMigrationsAsync();

await host.RunAsync();
