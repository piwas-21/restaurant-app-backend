using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
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

    /// <summary>
    /// How much further past <see cref="FileStorageSettings.MaxDecodePixels"/> an oversized JPEG
    /// may go, given its bitmap is bounded by the target box rather than the source. 4x takes the
    /// default 24 MP to 96 MP — comfortably past any current camera — while still refusing a header
    /// claiming an absurd canvas. Bytes are separately capped by MaxFileSizeBytes.
    /// </summary>
    private const int ScaledDecodeBudgetMultiplier = 4;

    private readonly FileStorageSettings _settings;
    private readonly ILogger<ImageSharpImageProcessor> _logger;

    public ImageSharpImageProcessor(IOptions<FileStorageSettings> settings, ILogger<ImageSharpImageProcessor> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Stream?> ProcessAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        // This overload opens the stream, so this overload closes it. The stream overload never
        // disposes what it was handed — the caller owns that.
        await using var stream = file.OpenReadStream();
        return await ProcessAsync(stream, file.FileName, cancellationToken);
    }

    public async Task<Stream?> ProcessAsync(Stream source, string fileName, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);
        if (!ProcessableExtensions.Contains(extension))
        {
            return null;
        }

        // We read the stream twice (probe, then decode), so it has to rewind. Uploads and
        // FileStreams both do; anything else gets buffered — bounded by the upload size limit the
        // command validator already enforces. Only the buffer we allocate is ours to dispose.
        MemoryStream? buffered = null;
        try
        {
            Stream input = source;
            if (!source.CanSeek)
            {
                buffered = await BufferAsync(source, cancellationToken);
                input = buffered;
            }

            var start = input.Position;

            // Cheap header read first — decide everything from the declared size before allocating
            // a bitmap.
            var info = await Image.IdentifyAsync(input, cancellationToken);
            var maxEdge = Math.Max(1, _settings.MaxImageEdgePixels);
            // Max(width, height) is orientation-invariant, so this is safe to decide pre-AutoOrient.
            var needsResize = Math.Max(info.Width, info.Height) > maxEdge;

            if ((long)info.Width * info.Height > EffectiveDecodePixelBudget(info, needsResize))
            {
                _logger.LogWarning(
                    "Skipping resize for {FileName}: {Width}x{Height} exceeds the decode guard",
                    fileName, info.Width, info.Height);
                return null;
            }

            input.Position = start;
            // Only an oversized source gets TargetSize — see DecoderOptionsFor: handing it a
            // smaller image would ENLARGE it.
            using var image = needsResize
                ? await Image.LoadAsync(ScaledDecoderOptions(maxEdge), input, cancellationToken)
                : await Image.LoadAsync(input, cancellationToken);

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
            _logger.LogWarning(ex, "Image processing failed for {FileName}; storing the original", fileName);
            return null;
        }
        finally
        {
            if (buffered is not null)
            {
                await buffered.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Decode straight into the target box when — and only when — the source is bigger than it.
    /// JPEG decodes natively at 1/2, 1/4 and 1/8 scale, so peak memory then tracks the OUTPUT
    /// rather than the input: a 40 MP photo costs about what a 1.7 MP one does.
    ///
    /// The `needsResize` condition is load-bearing, not an optimisation. `TargetSize` scales
    /// TOWARDS the box in both directions, so handing it a smaller image ENLARGES it — passing it
    /// unconditionally turned an 800px upload into 1600px of interpolated mush. (Caught by
    /// `ProcessAsync_SmallImage_IsNotUpscaled`, which is why that test exists.)
    /// </summary>
    private static DecoderOptions ScaledDecoderOptions(int maxEdge)
        => new() { TargetSize = new Size(maxEdge, maxEdge) };

    /// <summary>
    /// Pixel ceiling for the decompression-bomb guard. The guard exists because decoding costs
    /// ~4 bytes per SOURCE pixel, so a small file declaring a huge canvas can exhaust memory.
    ///
    /// That reasoning only holds for a full decode. When we scale on decode (an oversized JPEG),
    /// the bitmap is bounded by the target box instead, so the source pixel count stops driving
    /// memory and the plain limit is simply over-strict — it was refusing five of RUMI's real
    /// menu photos (up to 40.2 MP), leaving them served full-size. Formats that cannot scale on
    /// decode (PNG, WebP) keep the strict limit, because for them the original reasoning stands.
    /// </summary>
    private long EffectiveDecodePixelBudget(ImageInfo info, bool needsResize)
    {
        var scalesOnDecode = needsResize && info.Metadata.DecodedImageFormat is JpegFormat;
        return scalesOnDecode
            ? (long)_settings.MaxDecodePixels * ScaledDecodeBudgetMultiplier
            : _settings.MaxDecodePixels;
    }

    private static async Task<MemoryStream> BufferAsync(Stream source, CancellationToken cancellationToken)
    {
        var buffered = new MemoryStream();
        await source.CopyToAsync(buffered, cancellationToken);
        buffered.Position = 0;
        return buffered;
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
