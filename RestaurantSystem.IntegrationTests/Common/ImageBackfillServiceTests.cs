using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Features.Maintenance.Services;
using RestaurantSystem.Api.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// Retro-resize of images stored before resize-on-upload existed. Dry runs must leave every
/// original byte-identical and write a preview instead; only apply overwrites.
/// </summary>
public class ImageBackfillServiceTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "rumi-backfill-" + Guid.NewGuid().ToString("N"));

    private string UploadsRoot => Path.Combine(_contentRoot, "wwwroot", "uploads");

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private ImageBackfillService CreateService(string provider = "Local", int maxEdge = 1600)
    {
        var settings = Options.Create(new FileStorageSettings
        {
            Provider = provider,
            MaxImageEdgePixels = maxEdge,
            ImageQuality = 82,
            MaxDecodePixels = 24_000_000,
        });

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Returns(_contentRoot);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LocalStorage:BaseUrl"] = "https://cdn.test/uploads" })
            .Build();

        return new ImageBackfillService(
            new ImageSharpImageProcessor(settings, NullLogger<ImageSharpImageProcessor>.Instance),
            settings,
            environment.Object,
            configuration,
            NullLogger<ImageBackfillService>.Instance);
    }

    /// <summary>Photographic noise, so the encoder can't compress it away to nothing.</summary>
    private string WriteImage(string relativePath, int width, int height)
    {
        var path = Path.Combine(UploadsRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var image = new Image<Rgba32>(width, height);
        var random = new Random(42);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                }
            }
        });
        image.SaveAsJpeg(path);
        return path;
    }

    [Fact]
    public async Task RunAsync_DryRun_LeavesTheOriginalUntouchedAndWritesAPreview()
    {
        var path = WriteImage("products/big.jpg", 3000, 2000);
        var before = await File.ReadAllBytesAsync(path);

        var report = await CreateService().RunAsync(apply: false, maxFiles: 100);

        report.Applied.Should().BeFalse();
        report.FilesScanned.Should().Be(1);
        report.FilesChanged.Should().Be(1);

        var entry = report.Entries.Single();
        entry.Outcome.Should().Be("resized");
        entry.OriginalWidth.Should().Be(3000);
        entry.NewWidth.Should().Be(1600);
        entry.NewHeight.Should().Be(1067); // 2000 × 1600/3000, rounded — aspect preserved
        entry.BytesSaved.Should().BeGreaterThan(0);

        // The whole point of a dry run: judge it before anything is overwritten.
        (await File.ReadAllBytesAsync(path)).Should().Equal(before);
        entry.OriginalUrl.Should().Be("https://cdn.test/uploads/products/big.jpg");
        entry.PreviewUrl.Should().Be("https://cdn.test/uploads/_resize-preview/products/big.jpg");
        File.Exists(Path.Combine(UploadsRoot, ImageBackfillService.PreviewFolder, "products", "big.jpg"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_Apply_RewritesTheOriginalInPlace()
    {
        var path = WriteImage("products/big.jpg", 3000, 2000);
        var originalBytes = new FileInfo(path).Length;

        var report = await CreateService().RunAsync(apply: true, maxFiles: 100);

        report.Applied.Should().BeTrue();
        report.TotalBytesSaved.Should().BeGreaterThan(0);
        report.Entries.Single().PreviewUrl.Should().BeNull();

        new FileInfo(path).Length.Should().BeLessThan(originalBytes);
        var info = await Image.IdentifyAsync(path);
        info.Width.Should().Be(1600);
        // No stray .tmp left behind by the atomic write.
        Directory.EnumerateFiles(UploadsRoot, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_AlreadySmallImage_IsSkippedRatherThanInflated()
    {
        // Re-encoding an already-small JPEG at q82 typically grows it; that must never be written.
        var path = WriteImage("products/small.jpg", 320, 240);
        var before = await File.ReadAllBytesAsync(path);

        var report = await CreateService().RunAsync(apply: true, maxFiles: 100);

        report.Entries.Single().Outcome.Should().Be("skipped-no-gain");
        report.FilesChanged.Should().Be(0);
        report.FilesSkipped.Should().Be(1);
        report.TotalBytesSaved.Should().Be(0);
        (await File.ReadAllBytesAsync(path)).Should().Equal(before);
    }

    [Fact]
    public async Task RunAsync_DoesNotRecurseIntoItsOwnPreviews()
    {
        WriteImage("products/big.jpg", 3000, 2000);
        var service = CreateService();

        await service.RunAsync(apply: false, maxFiles: 100);
        var second = await service.RunAsync(apply: false, maxFiles: 100);

        second.FilesScanned.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_HonoursTheFileCapAndFlagsTruncation()
    {
        WriteImage("products/a.jpg", 2000, 2000);
        WriteImage("products/b.jpg", 2000, 2000);

        var report = await CreateService().RunAsync(apply: false, maxFiles: 1);

        report.FilesScanned.Should().Be(1);
        report.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task ClearPreviews_RemovesTheDryRunOutput()
    {
        WriteImage("products/big.jpg", 3000, 2000);
        var service = CreateService();
        await service.RunAsync(apply: false, maxFiles: 100);

        service.ClearPreviews().Should().Be(1);

        Directory.Exists(Path.Combine(UploadsRoot, ImageBackfillService.PreviewFolder)).Should().BeFalse();
        File.Exists(Path.Combine(UploadsRoot, "products", "big.jpg")).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_NonLocalProvider_IsRejected()
    {
        var act = () => CreateService(provider: "S3").RunAsync(apply: false, maxFiles: 100);

        await act.Should().ThrowAsync<BadRequestException>().WithMessage("*S3*");
    }
}
