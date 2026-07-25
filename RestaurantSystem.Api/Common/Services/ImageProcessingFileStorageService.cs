using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// Decorates the real <see cref="IFileStorageService"/> to downscale/recompress raster image
/// uploads before they are stored, so oversized originals never hit disk or the nightly backups.
/// The format (hence extension + the inner service's filename scheme + URL) is preserved; anything
/// the processor declines is stored untouched. All non-upload members delegate straight through.
/// </summary>
public class ImageProcessingFileStorageService : IFileStorageService
{
    private readonly IFileStorageService _inner;
    private readonly IImageProcessor _processor;

    public ImageProcessingFileStorageService(IFileStorageService inner, IImageProcessor processor)
    {
        _inner = inner;
        _processor = processor;
    }

    public async Task<string> UploadFileAsync(IFormFile file, string folder, string? fileName = null, CancellationToken cancellationToken = default)
    {
        var processed = await _processor.ProcessAsync(file, cancellationToken);
        if (processed is null)
        {
            return await _inner.UploadFileAsync(file, folder, fileName, cancellationToken);
        }

        await using (processed)
        {
            // Re-wrap the processed bytes so the inner service keeps ownership of filename
            // generation + content type; the extension (and thus the URL) is unchanged.
            var repacked = new FormFile(processed, 0, processed.Length, file.Name, file.FileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = file.ContentType,
            };
            return await _inner.UploadFileAsync(repacked, folder, fileName, cancellationToken);
        }
    }

    public Task<string> UploadFileAsync(Stream stream, string folder, string fileName, string contentType, CancellationToken cancellationToken = default)
        => _inner.UploadFileAsync(stream, folder, fileName, contentType, cancellationToken);

    public Task<bool> DeleteFileAsync(string fileUrl, CancellationToken cancellationToken = default)
        => _inner.DeleteFileAsync(fileUrl, cancellationToken);

    public Task<string> GetSignedUrlAsync(string fileKey, TimeSpan expirationTime, CancellationToken cancellationToken = default)
        => _inner.GetSignedUrlAsync(fileKey, expirationTime, cancellationToken);

    public Task<bool> FileExistsAsync(string fileUrl, CancellationToken cancellationToken = default)
        => _inner.FileExistsAsync(fileUrl, cancellationToken);

    public Task<FileMetadata?> GetFileMetadataAsync(string fileUrl, CancellationToken cancellationToken = default)
        => _inner.GetFileMetadataAsync(fileUrl, cancellationToken);
}
