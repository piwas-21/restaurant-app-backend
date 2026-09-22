using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

internal sealed record FloorSessionRow(
    Guid Id,
    Guid? TableId,
    int? TableNumber,
    string? Currency,
    int Version,
    DateTime OpenedAt,
    TableBillDto? Bill);

internal sealed record FloorOrderRow(
    Guid Id,
    Guid? ServiceSessionId,
    Guid? TableId,
    int? TableNumber,
    OrderStatus Status,
    decimal Total,
    decimal TotalPaid,
    decimal RemainingAmount,
    bool CanCollect);

internal sealed record ReservationRow(
    Guid Id,
    string CustomerName,
    DateTime Date,
    TimeSpan Start,
    TimeSpan End,
    int GuestCount,
    ReservationStatus Status,
    Guid TableId,
    List<Guid> CombinedTableIds)
{
    public IEnumerable<Guid> AllTableIds => CombinedTableIds.Prepend(TableId);
    public DateTime LocalStart => Date.Date + Start;
    public DateTime LocalEnd => Date.Date + End;
}

internal sealed record ServerFloorProjection(
    List<ServerFloorTableDto> Tables,
    string Version,
    DateTimeOffset? NextStateChangeAt);
