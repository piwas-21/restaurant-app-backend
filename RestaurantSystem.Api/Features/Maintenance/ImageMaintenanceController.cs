using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Maintenance.Dtos;
using RestaurantSystem.Api.Features.Maintenance.Interfaces;

namespace RestaurantSystem.Api.Features.Maintenance;

/// <summary>
/// Admin-only one-off maintenance over stored images. Resize-on-upload only ever applied to new
/// uploads; this brings everything older into line with the same settings.
/// </summary>
[ApiController]
[Route("api/maintenance/images")]
[Authorize(Roles = "Admin")]
public class ImageMaintenanceController : ControllerBase
{
    /// <summary>
    /// Ceiling on <c>maxFiles</c>. The work is synchronous, so one call must stay inside a sane
    /// request duration. A larger library is covered by paging with <c>continueFrom</c>, NOT by
    /// re-running — this used to claim the opposite, and it was wrong twice over (#280). A bare
    /// re-run restarts from the first file, and a "skip" is not cheap: <c>skipped-no-gain</c> is
    /// decided only AFTER a full decode and re-encode, so a skipped file costs almost what a
    /// rewritten one does and counts against this cap just the same.
    /// </summary>
    private const int MaxFilesPerRun = 500;

    /// <summary>
    /// Ceiling on <c>maxRows</c>: one card variant costs a full decode + re-encode of the
    /// original, the same per-file cost class the resize backfill's cap exists for.
    /// </summary>
    private const int MaxRowsPerRun = 300;

    private readonly IImageBackfillService _backfill;
    private readonly IProductCardVariantBackfillService _cardVariants;

    public ImageMaintenanceController(IImageBackfillService backfill, IProductCardVariantBackfillService cardVariants)
    {
        _backfill = backfill;
        _cardVariants = cardVariants;
    }

    /// <summary>
    /// Generates the card WebP for every product image that predates the feature. Dry-run
    /// (<c>apply=false</c>) reports counts only; <c>apply=true</c> writes
    /// <c>&lt;name&gt;-800.webp</c> beside each original and fills <c>ProductImage.CardUrl</c>.
    /// Re-running continues — filled rows drop out of the query.
    /// </summary>
    [HttpPost("card-variants")]
    public async Task<ApiResponse<ProductCardVariantReportDto>> BackfillCardVariants(
        [FromQuery] bool apply = false,
        [FromQuery] int maxRows = MaxRowsPerRun,
        CancellationToken cancellationToken = default)
    {
        var report = await _cardVariants.RunAsync(apply, maxRows, cancellationToken);
        return ApiResponse<ProductCardVariantReportDto>.SuccessWithData(report);
    }

    /// <summary>
    /// Report what the resize pipeline would do to the images already in storage.
    ///
    /// Defaults to a dry run: nothing is overwritten, and each resized candidate is written to the
    /// preview folder so <c>previewUrl</c> and <c>originalUrl</c> can be compared before deciding.
    /// Pass <c>apply=true</c> only once the previews look right — it overwrites the originals, and
    /// the only way back is the nightly backup.
    /// </summary>
    [HttpPost("backfill")]
    public async Task<ApiResponse<ImageBackfillReportDto>> Backfill(
        [FromQuery] bool apply = false,
        [FromQuery] int maxFiles = MaxFilesPerRun,
        [FromQuery] string? continueFrom = null,
        CancellationToken cancellationToken = default)
    {
        var capped = Math.Clamp(maxFiles, 1, MaxFilesPerRun);
        var report = await _backfill.RunAsync(apply, capped, continueFrom, cancellationToken);

        var message = apply
            ? $"Rewrote {report.FilesChanged} image(s), saving {report.TotalBytesSaved / 1024} KB."
            : $"Dry run: {report.FilesChanged} image(s) would shrink, saving {report.TotalBytesSaved / 1024} KB. "
              + "Compare previewUrl against originalUrl, then re-run with apply=true.";

        // Said plainly, because the truncation message is what the previous behaviour got wrong:
        // it invited a re-run that could not reach any further.
        if (report.Truncated)
        {
            message += $" Stopped at the {capped}-image cap; continue with continueFrom={report.NextCursor}.";
        }

        return ApiResponse<ImageBackfillReportDto>.SuccessWithData(report, message);
    }

    /// <summary>Delete the dry-run previews once they've been reviewed.</summary>
    [HttpDelete("backfill/previews")]
    public ApiResponse<int> ClearPreviews()
    {
        var removed = _backfill.ClearPreviews();
        return ApiResponse<int>.SuccessWithData(removed, $"Removed {removed} preview file(s).");
    }
}
