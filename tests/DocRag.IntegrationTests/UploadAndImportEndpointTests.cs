using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace DocRag.IntegrationTests;

public sealed class UploadAndImportEndpointTests : IClassFixture<ApiTestApplicationFactory>
{
    private readonly ApiTestApplicationFactory _factory;

    public UploadAndImportEndpointTests(ApiTestApplicationFactory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task Upload_ShouldRejectNonMultipartRequests()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        using var content = JsonContent.Create(new { file = "nope" });
        var response = await client.PostAsync("/api/documents/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_ShouldQueueSupportedDocument_AndCreateJob()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("hello doc rag")), "file", "guide.txt");

        var response = await client.PostAsync("/api/documents/upload", form);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        payload.GetProperty("status").GetString().Should().Be("queued");
        payload.GetProperty("duplicate").GetBoolean().Should().BeFalse();
        _factory.IngestionJobRepository.CreatedDocumentIds.Should().ContainSingle();
        _factory.ManagedFileStorage.PromotedStoredFileNames.Should().ContainSingle();
    }

    [Fact]
    public async Task Upload_ShouldReturnExistingDocument_ForDuplicateContent()
    {
        using var firstClient = _factory.CreateClient();
        firstClient.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        using var firstForm = new MultipartFormDataContent();
        firstForm.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("same content")), "file", "first.txt");
        var firstResponse = await firstClient.PostAsync("/api/documents/upload", firstForm);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var secondClient = _factory.CreateClient();
        secondClient.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        using var secondForm = new MultipartFormDataContent();
        secondForm.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("same content")), "file", "second.txt");
        var secondResponse = await secondClient.PostAsync("/api/documents/upload", secondForm);
        var payload = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();

        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.GetProperty("duplicate").GetBoolean().Should().BeTrue();
        _factory.IngestionJobRepository.CreatedDocumentIds.Should().ContainSingle();
        _factory.ManagedFileStorage.PromotedStoredFileNames.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportFolder_ShouldQueueSupportedFiles_AndSkipDuplicateContent()
    {
        Directory.CreateDirectory(_factory.ImportRoot);
        await File.WriteAllTextAsync(Path.Combine(_factory.ImportRoot, "alpha.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(_factory.ImportRoot, "beta.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(_factory.ImportRoot, "ignored.jpg"), "not supported");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        var response = await client.PostAsJsonAsync("/api/documents/import-folder", new { recursive = false });
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.GetProperty("queued").GetInt32().Should().Be(1);
        payload.GetProperty("skipped").GetInt32().Should().Be(1);
        payload.GetProperty("failed").GetInt32().Should().Be(0);
        payload.GetProperty("documents").GetArrayLength().Should().Be(1);
        payload.GetProperty("skippedFiles").GetArrayLength().Should().Be(1);
        _factory.IngestionJobRepository.CreatedDocumentIds.Should().ContainSingle();
        _factory.ManagedFileStorage.PromotedStoredFileNames.Should().ContainSingle();
    }
}
