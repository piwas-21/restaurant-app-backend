using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Maintenance.Dtos;
using RestaurantSystem.Api.Features.Maintenance.Interfaces;

namespace RestaurantSystem.Api.Features.Maintenance;

/// <summary>
/// Admin-only repair of missing product card variants, split from the resize backfill controller
/// (S6960): the two backfills share nothing but the "maintenance over stored images" theme.
/// Local provider only — the variant walk reads and writes the uploads directory directly.
/// </summary>
[ApiController]
[Route("api/maintenance/images")]
[Authorize(Roles = "Admin")]
public class CardVariantMaintenanceController(
    IProductCardVariantBackfillService cardVariants)
{
    /// <summary>
    /// Generates the card WebP for every product image that predates the feature. Dry-run
    /// (<c>apply=false</c>) reports counts only; <c>apply=true</c> writes
    /// <c>&lt;name&gt;-800.webp</c> beside each original and fills <c>ProductImage.CardUrl</c>.
    /// Continue with the returned NextCursor, including after a dry run or skipped rows.
    /// maxRows must be between 1 and 300; start without a cursor to retry skipped rows.
    /// </summary>
    [HttpPost("card-variants")]
    public async Task<ApiResponse<ProductCardVariantReportDto>> BackfillCardVariants(
        [FromQuery] bool apply = false,
        [FromQuery] int maxRows = IProductCardVariantBackfillService.MaxRowsPerRun,
        [FromQuery] string? continueFrom = null,
        CancellationToken cancellationToken = default)
    {
        var report = await cardVariants.RunAsync(apply, maxRows, continueFrom, cancellationToken);
        return ApiResponse<ProductCardVariantReportDto>.SuccessWithData(report);
    }
}
