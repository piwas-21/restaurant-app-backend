using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderKitchenChangeWriter
{
    /// <summary>Stages one destination's frozen delta in the caller's locked amendment transaction.
    /// It does not save, publish the whole order, or perform a printer network call.</summary>
    Task<Guid> StageAsync(Order order, Guid amendmentId, long? accountRevision,
        DevicePrintTarget target, IReadOnlyList<PrinterFeedChangeDto> changes,
        string summary, CancellationToken cancellationToken);
}
