using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Maintenance.Dtos;
using RestaurantSystem.Api.Features.Maintenance.Interfaces;
using RestaurantSystem.Api.Settings;
using SixLabors.ImageSharp;

namespace RestaurantSystem.Api.Features.Maintenance.Services;

/// <summary>
/// Local-disk implementation of <see cref="IImageBackfillService"/>. Reuses
/// <see cref="IImageProcessor"/> rather than re-deriving the resize, so a backfilled file is
/// byte-for-byte what a re-upload of the same source would produce.
/// </summary>
public class ImageBackfillService : IImageBackfillService
{
    /// <summary>
    /// Dry-run output lands here, under the uploads root so the existing Caddy file_server serves
    /// it for comparison with no extra plumbing. Excluded from the scan so repeat runs don't
    /// recurse into their own output.
    /// </summary>
    public const string PreviewFolder = "_resize-preview";

    private static readonly string[] ScannedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly IImageProcessor _processor;
    private readonly FileStorageSettings _settings;
    private readonly string _uploadsRoot;
    private readonly string _baseUrl;
    private readonly ILogger<ImageBackfillService> _logger;

    public ImageBackfillService(
        IImageProcessor processor,
        IOptions<FileStorageSettings> settings,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<ImageBackfillService> logger)
    {
        _processor = processor;
        _settings = settings.Value;
        // Same location LocalFileStorageService writes to — the bind-mounted uploads volume.
        _uploadsRoot = Path.Combine(environment.ContentRootPath, "wwwroot", "uploads");
        _baseUrl = (configuration["LocalStorage:BaseUrl"] ?? string.Empty).TrimEnd('/');
        _logger = logger;
    }

    public async Task<ImageBackfillReportDto> RunAsync(
        bool apply, int maxFiles, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_settings.Provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException(
                $"Image backfill walks the local uploads directory; the configured provider is '{_settings.Provider}'.");
        }

        var report = new ImageBackfillReportDto
        {
            Applied = apply,
            MaxImageEdgePixels = _settings.MaxImageEdgePixels,
            ImageQuality = _settings.ImageQuality,
        };

        if (!Directory.Exists(_uploadsRoot))
        {
            return report;
        }

        foreach (var path in EnumerateImages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (report.FilesScanned >= maxFiles)
            {
                report.Truncated = true;
                break;
            }

            report.FilesScanned++;
            var entry = await ProcessOneAsync(path, apply, cancellationToken);
            report.Entries.Add(entry);
            Tally(report, entry);
        }

        report.TotalBytesSaved = report.TotalOriginalBytes - report.TotalNewBytes;
        return report;
    }

    public int ClearPreviews()
    {
        var previewRoot = Path.Combine(_uploadsRoot, PreviewFolder);
        if (!Directory.Exists(previewRoot))
        {
            return 0;
        }

        var removed = Directory.EnumerateFiles(previewRoot, "*", SearchOption.AllDirectories).Count();
        Directory.Delete(previewRoot, recursive: true);
        _logger.LogInformation("Cleared {Count} image-backfill preview file(s)", removed);
        return removed;
    }

    private IEnumerable<string> EnumerateImages()
    {
        var previewRoot = Path.Combine(_uploadsRoot, PreviewFolder);
        return Directory
            .EnumerateFiles(_uploadsRoot, "*", SearchOption.AllDirectories)
            .Where(p => !p.StartsWith(previewRoot, StringComparison.Ordinal))
            .Where(p => ScannedExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal);
    }

    private async Task<ImageBackfillEntryDto> ProcessOneAsync(
        string path, bool apply, CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(_uploadsRoot, path).Replace('\\', '/');
        var originalBytes = new FileInfo(path).Length;
        var entry = new ImageBackfillEntryDto
        {
            RelativePath = relativePath,
            OriginalUrl = $"{_baseUrl}/{relativePath}",
            OriginalBytes = originalBytes,
            NewBytes = originalBytes,
            Outcome = "failed",
        };

        try
        {
            await using var source = File.OpenRead(path);
            var info = await Image.IdentifyAsync(source, cancellationToken);
            entry.OriginalWidth = info.Width;
            entry.OriginalHeight = info.Height;
            source.Position = 0;

            await using var processed = await _processor.ProcessAsync(source, path, cancellationToken);
            if (processed is null)
            {
                // Unprocessable format, or past the decompression-bomb guard — same call the
                // upload seam makes, and the same answer: leave the file alone.
                return Skip(entry, "skipped-unprocessable");
            }

            var newInfo = await Image.IdentifyAsync(processed, cancellationToken);
            processed.Position = 0;

            // Re-encoding an already-optimised file can inflate it. Never trade bytes for nothing.
            if (processed.Length >= originalBytes)
            {
                return Skip(entry, "skipped-no-gain");
            }

            entry.NewWidth = newInfo.Width;
            entry.NewHeight = newInfo.Height;
            entry.NewBytes = processed.Length;
            entry.Outcome = newInfo.Width == info.Width && newInfo.Height == info.Height
                ? "recompressed"
                : "resized";

            var destination = apply ? path : Path.Combine(_uploadsRoot, PreviewFolder, relativePath);
            // Both branches are rooted paths, so this is never null in practice — but assert it
            // rather than silencing the compiler with `!` (Sonar S8970).
            var destinationDirectory = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(destinationDirectory))
            {
                return Skip(entry, "skipped-unprocessable");
            }

            Directory.CreateDirectory(destinationDirectory);
            await WriteAsync(processed, destination, cancellationToken);

            if (!apply)
            {
                entry.PreviewUrl = $"{_baseUrl}/{PreviewFolder}/{relativePath}";
            }

            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image backfill failed for {RelativePath}; leaving it untouched", relativePath);
            entry.NewWidth = entry.OriginalWidth;
            entry.NewHeight = entry.OriginalHeight;
            return entry;
        }
    }

    private static ImageBackfillEntryDto Skip(ImageBackfillEntryDto entry, string outcome)
    {
        entry.NewWidth = entry.OriginalWidth;
        entry.NewHeight = entry.OriginalHeight;
        entry.NewBytes = entry.OriginalBytes;
        entry.Outcome = outcome;
        return entry;
    }

    private static async Task WriteAsync(Stream processed, string destination, CancellationToken cancellationToken)
    {
        // Write beside the target then move, so a crash mid-write can't leave a truncated image
        // where a valid one used to be.
        var temp = destination + ".tmp";
        await using (var file = File.Create(temp))
        {
            await processed.CopyToAsync(file, cancellationToken);
        }
        File.Move(temp, destination, overwrite: true);
    }

    private static void Tally(ImageBackfillReportDto report, ImageBackfillEntryDto entry)
    {
        report.TotalOriginalBytes += entry.OriginalBytes;
        report.TotalNewBytes += entry.NewBytes;

        switch (entry.Outcome)
        {
            case "resized":
            case "recompressed":
                report.FilesChanged++;
                break;
            case "failed":
                report.FilesFailed++;
                break;
            default:
                report.FilesSkipped++;
                break;
        }
    }
}
