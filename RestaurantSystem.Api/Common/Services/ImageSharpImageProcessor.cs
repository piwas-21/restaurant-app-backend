using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// <see cref="IImageProcessor"/> backed by SixLabors.ImageSharp. Stateless — safe as a singleton.
/// </summary>
public class ImageSharpImageProcessor : IImageProcessor
{
    // Only formats we can losslessly-enough round-trip. Animated GIF / SVG are passed through.
    private static readonly HashSet<string> ProcessableExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly FileStorageSettings _settings;
    private readonly ILogger<ImageSharpImageProcessor> _logger;

    public ImageSharpImageProcessor(IOptions<FileStorageSettings> settings, ILogger<ImageSharpImageProcessor> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Stream?> ProcessAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(file.FileName);
        if (!ProcessableExtensions.Contains(extension))
        {
            return null;
        }

        try
        {
            // Cheap header read first — bail before allocating a huge bitmap.
            await using (var probe = file.OpenReadStream())
            {
                var info = await Image.IdentifyAsync(probe, cancellationToken);
                if ((long)info.Width * info.Height > _settings.MaxDecodePixels)
                {
                    _logger.LogWarning(
                        "Skipping resize for {FileName}: {Width}x{Height} exceeds the decode guard",
                        file.FileName, info.Width, info.Height);
                    return null;
                }
            }

            await using var input = file.OpenReadStream();
            using var image = await Image.LoadAsync(input, cancellationToken);

            var maxEdge = Math.Max(1, _settings.MaxImageEdgePixels);
            // Max(width, height) is orientation-invariant, so this decision is safe pre-AutoOrient.
            var needsResize = Math.Max(image.Width, image.Height) > maxEdge;

            image.Mutate(ctx =>
            {
                ctx.AutoOrient(); // bake EXIF orientation in before we drop the profile
                if (needsResize)
                {
                    // Fit within the box preserving aspect. Upscaling is prevented by the
                    // needsResize guard above (Resize runs only when an edge exceeds maxEdge) —
                    // NOT by ResizeMode.Max, which would otherwise enlarge a smaller image.
                    ctx.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(maxEdge, maxEdge),
                    });
                }
            });

            // Shed EXIF/GPS/XMP/IPTC — smaller files and no location leakage.
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;

            var output = new MemoryStream();
            await image.SaveAsync(output, EncoderFor(extension), cancellationToken);
            output.Position = 0;
            return output;
        }
        catch (Exception ex)
        {
            // Fail open: an undecodable upload is stored as-is rather than rejected at this seam
            // (the command validator already screens type + size).
            _logger.LogWarning(ex, "Image processing failed for {FileName}; storing the original", file.FileName);
            return null;
        }
    }

    private IImageEncoder EncoderFor(string extension)
    {
        var quality = Math.Clamp(_settings.ImageQuality, 1, 100);
        return extension.ToLowerInvariant() switch
        {
            ".png" => new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression },
            ".webp" => new WebpEncoder { Quality = quality },
            _ => new JpegEncoder { Quality = quality },
        };
    }
}
