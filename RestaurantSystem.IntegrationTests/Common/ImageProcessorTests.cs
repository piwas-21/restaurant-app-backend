using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// WS4A resize-on-upload: <see cref="ImageSharpImageProcessor"/> downscales oversized raster
/// uploads to fit within the configured max edge, never upscales, preserves the format, and
/// passes non-raster files through untouched (returns null → the caller stores the original).
/// </summary>
public class ImageProcessorTests
{
    private static ImageSharpImageProcessor CreateProcessor(int maxEdge = 1600, int maxDecodePixels = 24_000_000) =>
        new(
            Options.Create(new FileStorageSettings
            {
                MaxImageEdgePixels = maxEdge,
                ImageQuality = 82,
                MaxDecodePixels = maxDecodePixels,
            }),
            NullLogger<ImageSharpImageProcessor>.Instance);

    private static FormFile MakeImage(int width, int height, string fileName, string contentType)
    {
        using var image = new Image<Rgba32>(width, height);
        var ms = new MemoryStream();
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".png":
                image.SaveAsPng(ms);
                break;
            case ".webp":
                image.SaveAsWebp(ms);
                break;
            default:
                image.SaveAsJpeg(ms);
                break;
        }
        ms.Position = 0;
        return new FormFile(ms, 0, ms.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private static async Task<(int Width, int Height)> DimensionsOf(Stream stream)
    {
        stream.Position = 0;
        using var image = await Image.LoadAsync(stream);
        return (image.Width, image.Height);
    }

    [Fact]
    public async Task ProcessAsync_OversizedImage_DownscalesToMaxEdge()
    {
        var file = MakeImage(3000, 2000, "big.jpg", "image/jpeg");

        await using var result = await CreateProcessor(1600).ProcessAsync(file);

        result.Should().NotBeNull();
        var (w, h) = await DimensionsOf(result!);
        Math.Max(w, h).Should().Be(1600);
        h.Should().BeInRange(1060, 1070, "the 3:2 aspect ratio must be preserved");
    }

    [Fact]
    public async Task ProcessAsync_SmallImage_IsNotUpscaled()
    {
        var file = MakeImage(800, 600, "small.jpg", "image/jpeg");

        await using var result = await CreateProcessor(1600).ProcessAsync(file);

        result.Should().NotBeNull();
        var (w, h) = await DimensionsOf(result!);
        w.Should().Be(800);
        h.Should().Be(600);
    }

    [Fact]
    public async Task ProcessAsync_PreservesPngFormat()
    {
        var file = MakeImage(2000, 2000, "art.png", "image/png");

        await using var result = await CreateProcessor(1600).ProcessAsync(file);

        result.Should().NotBeNull();
        result!.Position = 0;
        var format = await Image.DetectFormatAsync(result);
        format.Name.Should().Be("PNG");
    }

    [Fact]
    public async Task ProcessAsync_NonRasterExtension_ReturnsNull()
    {
        // Extension-gated before any decode, so the JPEG bytes under a .gif name still pass through.
        var file = MakeImage(100, 100, "animation.gif", "image/gif");

        var result = await CreateProcessor().ProcessAsync(file);

        result.Should().BeNull("GIF/SVG are stored as-is, not re-encoded");
    }

    [Fact]
    public async Task ProcessAsync_StripsExifMetadata()
    {
        using var source = new Image<Rgba32>(200, 200);
        source.Metadata.ExifProfile = new ExifProfile();
        source.Metadata.ExifProfile.SetValue(ExifTag.Copyright, "RUMI");
        var ms = new MemoryStream();
        source.SaveAsJpeg(ms);
        ms.Position = 0;
        var file = new FormFile(ms, 0, ms.Length, "file", "tagged.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg",
        };

        await using var result = await CreateProcessor().ProcessAsync(file);

        result.Should().NotBeNull();
        result!.Position = 0;
        using var output = await Image.LoadAsync(result);
        output.Metadata.ExifProfile.Should().BeNull("EXIF/GPS metadata must be stripped");
    }

    [Fact]
    public async Task ProcessAsync_ImageExceedingDecodeGuard_ReturnsNull()
    {
        var file = MakeImage(200, 200, "big.jpg", "image/jpeg"); // 40,000 px

        var result = await CreateProcessor(maxDecodePixels: 100).ProcessAsync(file);

        result.Should().BeNull("images above the decode-pixel guard are stored as-is, not decoded");
    }

    [Fact]
    public async Task ProcessAsync_UndecodableBytes_ReturnsNull()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("this is definitely not an image");
        var ms = new MemoryStream(bytes);
        var file = new FormFile(ms, 0, ms.Length, "file", "corrupt.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg",
        };

        var result = await CreateProcessor().ProcessAsync(file);

        result.Should().BeNull("a corrupt upload fails open and is stored as-is");
    }
}
