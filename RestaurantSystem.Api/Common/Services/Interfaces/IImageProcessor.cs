namespace RestaurantSystem.Api.Common.Services.Interfaces;

/// <summary>
/// Downscales and recompresses uploaded raster images at the storage seam, so oversized
/// originals never reach disk or the nightly backups. The image format (hence the file
/// extension) is preserved.
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    /// Resize-to-fit + recompress + strip metadata for JPEG/PNG/WebP uploads. Returns a
    /// rewound stream to store, or <c>null</c> when the file is not a processable raster image
    /// (or is unsafe/undecodable), in which case the caller stores the original untouched.
    /// </summary>
    Task<Stream?> ProcessAsync(IFormFile file, CancellationToken cancellationToken = default);
}
