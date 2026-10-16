namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>A complete visit or original order with its amendments, in the cashier queue.</summary>
public sealed record CashierOrderGroupDto(
    string GroupKey,
    Guid? ServiceSessionId,
    int? TableNumber,
    DateTime? ReleasedAt,
    bool IsArchivedFromTable,
    List<OrderDto> Orders)
{
    public bool HasUpdates { get; init; }
}
