namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record AccountPaymentAllocationDto(
    Guid OrderId,
    Guid? OrderItemId,
    int StartOrdinal,
    int UnitCount,
    long MinorPerUnit,
    long AmountMinor);
