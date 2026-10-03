namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record AccountEqualSharePlanDto(
    Guid ServiceSessionId,
    Guid PlanId,
    Guid OperationId,
    long AccountRevision,
    long TotalMinor,
    int ShareCount,
    string Currency,
    DateTime CreatedAt,
    DateTime? InvalidatedAt,
    IReadOnlyList<AccountPaymentAllocationDto> Scope);
