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
        // The resume point, which is what makes the truncation actionable rather than terminal.
        report.NextCursor.Should().Be("products/a.jpg");
    }

    // ---- #280: paging, i.e. whether the library is reachable at all -------------------------

    // A bare re-run does NOT continue — it restarts from the first file and the cap counts every
    // file processed, so image maxFiles+1 was unreachable however often the endpoint was called.
    // This is the defect stated as a test: the SAME call twice sees the same image both times.
    [Fact]
    public async Task RunAsync_WithoutACursor_RestartsFromTheBeginning()
    {
        WriteImage("products/a.jpg", 2000, 2000);
        WriteImage("products/b.jpg", 2000, 2000);
        var service = CreateService();

        var first = await service.RunAsync(apply: false, maxFiles: 1);
        var second = await service.RunAsync(apply: false, maxFiles: 1);

        first.Entries.Single().RelativePath.Should().Be("products/a.jpg");
        second.Entries.Single().RelativePath.Should().Be("products/a.jpg");
    }

    [Fact]
    public async Task RunAsync_WithACursor_ResumesStrictlyAfterIt()
    {
        WriteImage("products/a.jpg", 2000, 2000);
        WriteImage("products/b.jpg", 2000, 2000);

        var report = await CreateService().RunAsync(apply: false, maxFiles: 1, continueFrom: "products/a.jpg");

        // STRICTLY after: including the cursor would re-process one image per call, and at
        // maxFiles = 1 the walk would never advance at all.
        report.Entries.Single().RelativePath.Should().Be("products/b.jpg");
    }

    // The point of the whole change: every image is reachable by paging, including one past the
    // cap. Walks a 5-image library one file at a time and asserts it saw all five exactly once —
    // the property the issue says is impossible today, not a proxy for it.
    [Fact]
    public async Task RunAsync_PagedByCursor_ReachesEveryImageExactlyOnce()
    {
        var expected = new[] { "a.jpg", "b.jpg", "c.jpg", "d.jpg", "e.jpg" };
        foreach (var name in expected)
        {
            WriteImage($"products/{name}", 900, 900);
        }

        var service = CreateService();
        var seen = new List<string>();
        string? cursor = null;

        // Bounded well above the 5 pages needed, so a non-advancing cursor ends the loop by
        // exhausting the budget rather than hanging the suite — and fails on the count below.
        for (var page = 0; page < 20; page++)
        {
            var report = await service.RunAsync(apply: false, maxFiles: 1, continueFrom: cursor);
            seen.AddRange(report.Entries.Select(e => e.RelativePath));

            if (!report.Truncated)
            {
                // A finished walk reports no resume point; anything else would read as "there is
                // more" forever.
                report.NextCursor.Should().BeNull();
                break;
            }

            report.NextCursor.Should().NotBeNull();
            cursor = report.NextCursor;
        }

        seen.Should().Equal(expected.Select(n => $"products/{n}"));
    }

    // A skipped file must still advance the cursor. Skips count against the cap — `skipped-no-gain`
    // is decided only AFTER a full decode and re-encode — so a cursor that advanced only past
    // CHANGED files would hand back a resume point already behind the cap and re-walk the same
    // window forever. Every image here is tiny enough that re-encoding cannot beat the original.
    [Fact]
    public async Task RunAsync_AdvancesThroughSkippedFilesToo()
    {
        WriteImage("products/a.jpg", 8, 8);
        WriteImage("products/b.jpg", 8, 8);

        var service = CreateService();
        var first = await service.RunAsync(apply: false, maxFiles: 1);

        first.FilesChanged.Should().Be(0, "a tiny image cannot be shrunk further");
        first.FilesSkipped.Should().Be(1);
        first.NextCursor.Should().Be("products/a.jpg");

        var second = await service.RunAsync(apply: false, maxFiles: 1, continueFrom: first.NextCursor);
        second.Entries.Single().RelativePath.Should().Be("products/b.jpg");
    }

    // Ordering and resumption must share one string space. These two names straddle the boundary
    // that exposes a mismatch — 'products-1.jpg' vs 'products/a.jpg', where '-' (0x2D) sorts before
    // '/' (0x2F) — so resuming against absolute paths while sorting on relative ones loses the
    // second file here on any platform.
    //
    // What this canNOT reach on Linux CI is the narrower Windows form of the same bug: there the
    // absolute path is '\'-separated (0x5C) and sorts AFTER '/', so absolute and relative orderings
    // genuinely diverge. On POSIX they coincide, so that half rests on the scanner keeping the two
    // as one string rather than on this test.
    [Fact]
    public async Task RunAsync_OrdersAndResumesOnTheSamePathForm()
    {
        WriteImage("products-1.jpg", 900, 900);
        WriteImage("products/a.jpg", 900, 900);

        var service = CreateService();
        var first = await service.RunAsync(apply: false, maxFiles: 1);
        var second = await service.RunAsync(apply: false, maxFiles: 1, continueFrom: first.NextCursor);

        first.Entries.Single().RelativePath.Should().Be("products-1.jpg");
        second.Entries.Single().RelativePath.Should().Be("products/a.jpg");
        second.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_CompletedWalk_ReportsNoResumePoint()
    {
        WriteImage("products/only.jpg", 900, 900);

        var report = await CreateService().RunAsync(apply: false, maxFiles: 100);

        report.Truncated.Should().BeFalse();
        report.NextCursor.Should().BeNull();
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

    /// <summary>
    /// The guard that stops a silently-failed decode being written over a good photo. Thresholds
    /// come from RUMI's real library (2026-07-25): healthy re-encodes measured 0.6–3.1x density
    /// drop, the one image the decoder mangled measured 16.7x.
    /// </summary>
    [Theory]
    // healthy: 3000x2000 @ 4.6MB -> 1600x1067 @ 420KB is a ~3.1x drop — must pass
    [InlineData(4_823_449L, 3000, 2000, 430_080L, 1600, 1067, false)]
    // healthy: barely-compressible source, output denser than input — must pass
    [InlineData(1_855_425L, 3000, 2000, 149_504L, 1600, 1067, false)]
    // the real corruption: 3000x1999 @ 4.26MB -> 1600x1066 @ 74KB is a 16.7x drop
    [InlineData(4_467_668L, 3000, 1999, 75_776L, 1600, 1066, true)]
    // degenerate: an empty result is never trustworthy
    [InlineData(4_467_668L, 3000, 1999, 0L, 1600, 1066, true)]
    public void IsImplausiblySparse_SeparatesRealCompressionFromAFailedDecode(
        long ob, int ow, int oh, long nb, int nw, int nh, bool expected)
    {
        ImageBackfillService.IsImplausiblySparse(ob, ow, oh, nb, nw, nh).Should().Be(expected);
    }

    [Fact]
    public async Task RunAsync_OversizedImage_IsNowResizedInsteadOfRefused()
    {
        // 4016x6016 = 24.2 MP — over the 24 MP decode guard, so this used to come back
        // "skipped-unprocessable" and stay full-size. Scaled decoding means it no longer has to.
        WriteImage("products/huge.jpg", 4016, 6016);

        var report = await CreateService().RunAsync(apply: true, maxFiles: 10);

        var entry = report.Entries.Single();
        entry.Outcome.Should().Be("resized");
        Math.Max(entry.NewWidth, entry.NewHeight).Should().Be(1600);
        entry.BytesSaved.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RunAsync_NonLocalProvider_IsRejected()
    {
        var act = () => CreateService(provider: "S3").RunAsync(apply: false, maxFiles: 100);

        await act.Should().ThrowAsync<BadRequestException>().WithMessage("*S3*");
    }
}
