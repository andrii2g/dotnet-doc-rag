using DocRag.Core;
using DocRag.Infrastructure;
using DocRag.Infrastructure.Configuration;
using DocRag.Infrastructure.Database;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDocRagCore();
builder.Services.AddDocRagInfrastructure(builder.Configuration);

var app = builder.Build();

await app.Services.ApplyDocRagMigrationsAsync();

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "docs";
    options.SwaggerEndpoint("/openapi/v1.json", "DocRag API v1");
});

app.Run();
