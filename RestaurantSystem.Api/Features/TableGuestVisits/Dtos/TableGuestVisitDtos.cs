namespace RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

public sealed record TableGuestAdmissionCodeDto(string AdmissionCode, DateTime ExpiresAt);

public sealed record TableGuestJoinDto(Guid ServiceSessionId, string ParticipantToken, DateTime ExpiresAt);

public sealed record TableGuestOrderDto(
    Guid OrderId,
    string OrderNumber,
    string Status,
    string PaymentStatus,
    DateTime OrderDate,
    decimal Total,
    decimal TotalPaid,
    decimal RemainingAmount);

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
    int UnitCount,
    TableGuestItemDto Item);

public sealed record TableGuestAccountDto(
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
