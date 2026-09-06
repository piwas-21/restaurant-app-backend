using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Maintenance.Dtos;
using RestaurantSystem.Api.Features.Maintenance.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Maintenance.Services;

/// <summary>
/// Bounded local-only repair of missing product cards. Continue with the returned cursor, not a
/// bare rerun: missing and failed rows consume the page too. Start a fresh walk to retry them.
/// </summary>
public class ProductCardVariantBackfillService : IProductCardVariantBackfillService
{
    private readonly ApplicationDbContext _context;
    private readonly IImageProcessor _processor;
    private readonly FileStorageSettings _settings;
    private readonly string _uploadsRoot;
    private readonly string _baseUrl;
    private readonly ILogger<ProductCardVariantBackfillService> _logger;

    public ProductCardVariantBackfillService(
        ApplicationDbContext context,
        IImageProcessor processor,
        IOptions<FileStorageSettings> settings,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<ProductCardVariantBackfillService> logger)
    {
        _context = context;
        _processor = processor;
        _settings = settings.Value;
        _uploadsRoot = Path.Combine(environment.ContentRootPath, "wwwroot", "uploads");
        _baseUrl = (configuration["LocalStorage:BaseUrl"] ?? string.Empty).TrimEnd('/');
        _logger = logger;
    }

    public async Task<ProductCardVariantReportDto> RunAsync(
        bool apply, int maxRows, string? continueFrom = null, CancellationToken cancellationToken = default)
    {
        if (maxRows is < 1 or > IProductCardVariantBackfillService.MaxRowsPerRun)
        {
            throw new BadRequestException($"maxRows must be between 1 and {IProductCardVariantBackfillService.MaxRowsPerRun}.");
        }
        if (!string.Equals(_settings.Provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("Card-variant backfill requires the Local storage provider.");
        }
        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
            || baseUri.UserInfo.Length != 0 || baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0)
        {
            throw new BadRequestException("Card-variant backfill requires a valid LocalStorage:BaseUrl.");
        }

        var cursor = ProductCardVariantCursor.Parse(continueFrom);
        var paths = new ProductCardVariantPaths(_uploadsRoot, baseUri);
        var query = _context.ProductImages.AsNoTracking().Where(i => !i.IsDeleted && i.CardUrl == null);
        if (cursor is not null)
        {
            query = query.Where(i => i.CreatedAt > cursor.CreatedAt
                || (i.CreatedAt == cursor.CreatedAt && i.Id.CompareTo(cursor.Id) > 0));
        }

        var rows = await query.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .Take(maxRows + 1)
            .Select(i => new { i.Id, i.ProductId, i.Url, i.CreatedAt })
            .ToListAsync(cancellationToken);
        var report = new ProductCardVariantReportDto { Applied = apply, Truncated = rows.Count > maxRows };

        foreach (var row in rows.Take(maxRows))
        {
            cancellationToken.ThrowIfCancellationRequested();
            report.RowsScanned++;
            string? cardUrl = null;
            try
            {
                var location = paths.Resolve(row.Url, row.ProductId);
                cardUrl = await ProcessOneAsync(location, paths, apply, report, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Database failures and caller cancellation are not per-file skips. Keep them
                // outside this boundary; malformed keys and filesystem races must not stop a page.
                report.RowsFailed++;
                report.FailedImageIds.Add(row.Id);
                _logger.LogWarning(ex, "Card-variant backfill failed for image {ImageId}", row.Id);
            }

            if (cardUrl is not null)
            {
                // A concurrent delete or successful attachment wins. Do not resurrect or replace it.
                await _context.ProductImages
                    .Where(i => i.Id == row.Id && i.Url == row.Url && i.CardUrl == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(i => i.CardUrl, cardUrl), cancellationToken);
            }

            report.NextCursor = new ProductCardVariantCursor(row.CreatedAt, row.Id).ToString();
        }

        if (!report.Truncated)
        {
            report.NextCursor = null;
        }
        return report;
    }

    private async Task<string?> ProcessOneAsync(
        CardVariantLocation location, ProductCardVariantPaths paths, bool apply,
        ProductCardVariantReportDto report, CancellationToken cancellationToken)
    {
        if (File.Exists(location.VariantPath)
            && await ProductCardVariantFile.IsValidAsync(location.VariantPath, cancellationToken))
        {
            report.AlreadyPresent++;
            return apply ? location.CardUrl : null;
        }
        if (!File.Exists(location.OriginalPath))
        {
            report.SkippedMissingFile++;
            return null;
        }
        if (!apply)
        {
            // Dry-run validates an existing derivative but never decodes the original or writes.
            return null;
        }

        paths.EnsureSafe(location.OriginalPath);
        await using var source = File.OpenRead(location.OriginalPath);
        await using var variant = await _processor.GenerateCardVariantAsync(
            source, location.FileName, ProductImageCardVariants.EdgePixels, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (variant is null)
        {
            report.SkippedUndecodable++;
            return null;
        }

        await ProductCardVariantFile.PublishAsync(variant, location.VariantPath, paths, cancellationToken);
        report.VariantsCreated++;
        return location.CardUrl;
    }
}
