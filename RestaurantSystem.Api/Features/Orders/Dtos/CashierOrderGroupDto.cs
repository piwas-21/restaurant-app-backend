namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>A complete open visit, or one standalone order, in the cashier queue.</summary>
public sealed record CashierOrderGroupDto(
    string GroupKey,
    Guid? ServiceSessionId,
    int? TableNumber,
    DateTime? ReleasedAt,
    bool IsArchivedFromTable,
    List<OrderDto> Orders);
