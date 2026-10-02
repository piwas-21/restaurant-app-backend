using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Frozen preparation delta, never a request to reprint the current order.</summary>
public record PrinterFeedChangeDto
{
    public KitchenChangeKind Kind { get; init; }
    public OrderItemDto? Previous { get; init; }
    public OrderItemDto? Current { get; init; }
    /// <summary>
    /// For Replace only: the separately released supplement ticket that prepares Current. The
    /// printer renders Current here as a linked reference, never as a second preparation action.
    /// </summary>
    public Guid? ReplacementDispatchedOrderId { get; init; }
    public string? ReplacementDispatchedOrderNumber { get; init; }
}
