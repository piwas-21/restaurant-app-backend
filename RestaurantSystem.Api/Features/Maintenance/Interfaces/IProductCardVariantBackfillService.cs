using RestaurantSystem.Api.Features.Maintenance.Dtos;

namespace RestaurantSystem.Api.Features.Maintenance.Interfaces;

public interface IProductCardVariantBackfillService
{
    public const int MaxRowsPerRun = 300;

    /// <summary>
    /// Walks rows without CardUrl in stable upload order. maxRows must be 1..300. Pass the
    /// report's NextCursor as continueFrom to advance past ALL inspected rows, including skips
    /// and failures. Dry-run never writes. A fresh walk retries rows skipped by an earlier walk.
    /// </summary>
    Task<ProductCardVariantReportDto> RunAsync(
        bool apply, int maxRows, string? continueFrom = null, CancellationToken cancellationToken = default);
}
