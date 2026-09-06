using RestaurantSystem.Api.Features.Maintenance.Dtos;

namespace RestaurantSystem.Api.Features.Maintenance.Interfaces;

public interface IProductCardVariantBackfillService
{
    /// <summary>
    /// Generates card variants for <c>ProductImage</c> rows that predate the feature
    /// (<c>CardUrl == null</c>). <paramref name="apply"/> = false reports what WOULD happen
    /// without writing. Rows are walked in upload order; the run ends at <paramref name="maxRows"/>
    /// with <c>Truncated</c>, so re-running continues — filled rows drop out of the query.
    /// </summary>
    Task<ProductCardVariantReportDto> RunAsync(bool apply, int maxRows, CancellationToken cancellationToken = default);
}
