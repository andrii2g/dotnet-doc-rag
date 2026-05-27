using DocRag.Infrastructure.Configuration;
using DocRag.Infrastructure.Documents;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DocRag.UnitTests;

public sealed class LocalManagedFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnet-doc-rag-storage-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetManagedPath_ShouldRejectPathThatEscapesStorageRoot()
    {
        var storage = CreateStorage();

        var act = () => storage.GetManagedPath("..\\..\\outside.txt");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*escapes storage root*");
    }

    [Fact]
    public async Task StoreImportFileAsync_ShouldRejectPathOutsideImportRoot()
    {
        Directory.CreateDirectory(_root);
        var outsideFile = Path.Combine(_root, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");

        var storage = CreateStorage();

        var act = () => storage.StoreImportFileAsync(outsideFile, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*escapes the configured import root*");
    }

    [Fact]
    public async Task StoreUploadAsync_ShouldStageFileAndComputeMetadata()
    {
        var storage = CreateStorage();
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello"));

        var result = await storage.StoreUploadAsync(stream, "note.txt", "text/plain", CancellationToken.None);

        result.OriginalFileName.Should().Be("note.txt");
        result.Extension.Should().Be(".txt");
        result.ContentType.Should().Be("text/plain");
        result.SizeBytes.Should().Be(5);
        result.ContentSha256.Should().Be("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
        File.Exists(result.TempFilePath).Should().BeTrue();

        File.Delete(result.TempFilePath);
    }

    private LocalManagedFileStorage CreateStorage()
    {
        var options = Options.Create(new AppOptions
        {
            StoragePath = Path.Combine(_root, "storage", "documents"),
            ImportPath = Path.Combine(_root, "import"),
            MaxUploadBytes = 1024,
            AllowedExtensions = [".txt"]
        });

        Directory.CreateDirectory(options.Value.ImportPath);

        return new LocalManagedFileStorage(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
