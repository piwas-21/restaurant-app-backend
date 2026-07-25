using FluentAssertions;
using Microsoft.AspNetCore.Http;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// WS4A: the storage decorator uploads the RE-ENCODED bytes when the processor resizes, and the
/// ORIGINAL file untouched when the processor declines — preserving the filename/extension either
/// way so the inner service's URL scheme is unchanged.
/// </summary>
public class ImageProcessingFileStorageServiceTests
{
    [Fact]
    public async Task Upload_WhenProcessed_ForwardsAReencodedFilePreservingTheName()
    {
        var original = MakeFile("photo.jpg");
        var inner = new RecordingStorage();
        var decorator = new ImageProcessingFileStorageService(
            inner, new StubProcessor(new MemoryStream(new byte[] { 1, 2, 3 })));

        await decorator.UploadFileAsync(original, "products/1");

        inner.LastFile.Should().NotBeNull();
        inner.LastFile.Should().NotBeSameAs(original, "the re-encoded bytes are uploaded, not the original");
        inner.LastFile!.FileName.Should().Be("photo.jpg", "extension/URL must be preserved");
    }

    [Fact]
    public async Task Upload_WhenProcessorDeclines_ForwardsTheOriginal()
    {
        var original = MakeFile("clip.gif");
        var inner = new RecordingStorage();
        var decorator = new ImageProcessingFileStorageService(inner, new StubProcessor(null));

        await decorator.UploadFileAsync(original, "products/1");

        inner.LastFile.Should().BeSameAs(original);
    }

    private static FormFile MakeFile(string name)
    {
        var ms = new MemoryStream(new byte[] { 9, 9, 9 });
        return new FormFile(ms, 0, ms.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg",
        };
    }

    private sealed class StubProcessor(Stream? result) : IImageProcessor
    {
        public Task<Stream?> ProcessAsync(IFormFile file, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);

        public Task<Stream?> ProcessAsync(Stream source, string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class RecordingStorage : IFileStorageService
    {
        public IFormFile? LastFile { get; private set; }

        public Task<string> UploadFileAsync(IFormFile file, string folder, string? fileName = null, CancellationToken cancellationToken = default)
        {
            LastFile = file;
            return Task.FromResult("https://example.test/url");
        }

        public Task<string> UploadFileAsync(Stream stream, string folder, string fileName, string contentType, CancellationToken cancellationToken = default) =>
            Task.FromResult("https://example.test/url");

        public Task<bool> DeleteFileAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string> GetSignedUrlAsync(string fileKey, TimeSpan expirationTime, CancellationToken cancellationToken = default) => Task.FromResult(fileKey);
        public Task<bool> FileExistsAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<FileMetadata?> GetFileMetadataAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult<FileMetadata?>(null);
    }
}
