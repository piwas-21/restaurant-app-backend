namespace RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

public sealed record TableGuestJoinDto(Guid ServiceSessionId, string ParticipantToken, DateTime ExpiresAt);

public sealed partial record TableGuestOrderDto(
    Guid OrderId,
    string OrderNumber,
    string Status,
    string PaymentStatus,
    DateTime OrderDate,
    decimal Total,
    decimal TotalPaid,
    decimal RemainingAmount)
{
    public long DailyOrderNumber { get; init; }
    public DateOnly BusinessDate { get; init; }
}

public sealed record TableGuestIngredientDto(
    string Name,
    int Quantity,
    bool Removed,
    bool AddOn);

public sealed record TableGuestItemDto(
    Guid ItemId,
    string ProductName,
    string? VariationName,
    int Quantity,
    decimal UnitPrice,
    decimal ItemTotal,
    IReadOnlyList<TableGuestIngredientDto> IngredientCustomizations,
    IReadOnlyList<TableGuestItemDto> SideItems);

public sealed record TableGuestAccountLineDto(
    Guid OrderId,
    string OrderNumber,
    long DailyOrderNumber,
    DateOnly BusinessDate,
    int UnitCount,
    TableGuestItemDto Item);

public sealed partial record TableGuestAccountDto(
    Guid ServiceSessionId,
    string? TableLabel,
    string? Currency,
    long AccountRevision,
    decimal SubTotal,
    decimal Tax,
    decimal Discount,
    decimal Tip,
    decimal Total,
    decimal TotalPaid,
    decimal Remaining,
    decimal Credit,
    IReadOnlyList<TableGuestOrderDto> Orders,
    IReadOnlyList<TableGuestAccountLineDto> Items);
