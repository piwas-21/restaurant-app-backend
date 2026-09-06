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

    /// <summary>
    /// Stream overload of <see cref="ProcessAsync(IFormFile, CancellationToken)"/>, for callers
    /// holding bytes rather than a request upload — notably the backfill over images already on
    /// disk, which must produce exactly what a fresh upload would so a backfilled file and a
    /// re-uploaded one are indistinguishable. <paramref name="fileName"/> supplies the extension
    /// that picks the encoder. A non-seekable stream is buffered first.
    /// </summary>
    Task<Stream?> ProcessAsync(Stream source, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// A CARD-SIZED WebP derivative of a stored original (menu cards render at 120-400 CSS px;
    /// served originals run 800-1600 px, which is 8.3-16.4 MB per All view across tenants —
    /// partner-reported slowness, 2026-09-06). Fits within <paramref name="maxEdge"/>, never
    /// enlarges, always encodes WebP regardless of the source format: a PNG photo at this size
    /// is the single largest win. Returns a rewound stream, or <c>null</c> when the source is
    /// not a decodable raster image — the caller then serves the original, exactly as
    /// <see cref="ProcessAsync"/>'s fail-open contract does.
    /// </summary>
    Task<Stream?> GenerateCardVariantAsync(Stream source, string fileName, int maxEdge, CancellationToken cancellationToken = default);
}
