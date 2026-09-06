using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Maintenance.Dtos;
using RestaurantSystem.Api.Features.Maintenance.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Maintenance.Services;

/// <summary>
/// Generates card variants for product images stored BEFORE the feature existed. Walks the
/// <c>ProductImage</c> TABLE (not the directory, unlike <see cref="ImageBackfillService"/>): a row
/// without <c>CardUrl</c> is exactly the backlog, and a re-run is naturally idempotent because
/// filled rows drop out of the query.
/// </summary>
/// <remarks>
/// Local provider only, for the same reason the resize backfill is: it reads and writes the
/// uploads directory directly, which only <c>LocalStorage</c> means anything for.
/// </remarks>
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
        bool apply, int maxRows, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_settings.Provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The card-variant backfill reads the local uploads directory; the configured provider is '{_settings.Provider}'.");
        }

        var report = new ProductCardVariantReportDto { Applied = apply };

        var rows = await _context.ProductImages
            .AsNoTracking()
            .Where(i => !i.IsDeleted && i.CardUrl == null)
            .OrderBy(i => i.CreatedAt)
            .Take(maxRows)
            .Select(i => new { i.Id, i.Url })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            report.RowsScanned++;

            // Url carries the LocalStorage base + "products/<id>/<file>"; the filename and the
            // folder come from the SAME derivation the upload path used, so a variant written
            // here is byte-for-byte where a fresh upload would have put it.
            var fileName = Path.GetFileName(new Uri(row.Url).AbsolutePath);
            var folder = Path.GetDirectoryName(new Uri(row.Url).AbsolutePath.TrimStart('/').Replace("uploads/", ""))!
                .Replace('\\', '/');
            var variantName = ProductImageCardVariants.VariantFileName(fileName);
            var variantRelative = $"{folder}/{variantName}";
            var variantPath = Path.Combine(_uploadsRoot, variantRelative);

            if (File.Exists(variantPath))
            {
                report.AlreadyPresent++;
                if (apply)
                {
                    await AttachCardUrlAsync(row.Id, variantRelative, cancellationToken);
                }
                continue;
            }

            var originalPath = Path.Combine(_uploadsRoot, folder, fileName);
            if (!File.Exists(originalPath))
            {
                report.SkippedMissingFile++;
                continue;
            }

            if (!apply)
            {
                // Dry run stops here: nothing written, nothing decoded.
                continue;
            }

            await using var source = File.OpenRead(originalPath);
            var variant = await _processor.GenerateCardVariantAsync(
                source, fileName, ProductImageCardVariants.EdgePixels, cancellationToken);
            if (variant is null)
            {
                report.SkippedUndecodable++;
                continue;
            }

            await using (variant)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(variantPath)!);
                await using var target = File.Create(variantPath);
                await variant.CopyToAsync(target, cancellationToken);
            }

            await AttachCardUrlAsync(row.Id, variantRelative, cancellationToken);
            report.VariantsCreated++;
        }

        report.Truncated = report.RowsScanned >= maxRows;
        return report;
    }

    private async Task AttachCardUrlAsync(Guid imageId, string variantRelative, CancellationToken cancellationToken)
    {
        await _context.ProductImages
            .Where(i => i.Id == imageId)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.CardUrl, $"{_baseUrl}/{variantRelative}"), cancellationToken);
    }
}
