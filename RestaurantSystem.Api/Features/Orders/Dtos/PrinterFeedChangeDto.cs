using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Frozen preparation delta, never a request to reprint the current order.</summary>
public record PrinterFeedChangeDto
{
    public KitchenChangeKind Kind { get; init; }
    public OrderItemDto? Previous { get; init; }
    public OrderItemDto? Current { get; init; }
}
