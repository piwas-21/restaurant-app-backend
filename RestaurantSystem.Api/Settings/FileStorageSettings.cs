using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public class FileStorageSettings
{
    public const string SectionName = "FileStorage";

    public string Provider { get; set; } = "Local"; // "S3", "Azure", "Local"
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024; // 5MB
    public string[] AllowedExtensions { get; set; } = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    public string[] AllowedMimeTypes { get; set; } = { "image/jpeg", "image/png", "image/gif", "image/webp" };

    // Resize-on-upload (ImageSharp): raster uploads are downscaled to fit within this
    // longest-edge pixel bound and re-encoded at this quality (JPEG/WebP) before storage.
    [Range(64, 10000)]
    public int MaxImageEdgePixels { get; set; } = 1600;

    [Range(1, 100)]
    public int ImageQuality { get; set; } = 82;

    // Decompression-bomb guard: images whose declared pixel count exceeds this are stored as-is
    // rather than fully decoded (a 24 MP camera is ~24M px). Bounds transient decode RAM.
    [Range(1_000_000, int.MaxValue)]
    public int MaxDecodePixels { get; set; } = 24_000_000;
}
