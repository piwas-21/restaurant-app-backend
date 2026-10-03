using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record AccountPaymentEqualShareSummaryDto(
    Guid PlanId,
    long AccountRevision,
    long TotalMinor,
    int ShareCount,
    string Currency,
    bool IsOwnPlan,
    IReadOnlyList<AccountEqualShareSlotSummaryDto> Slots,
    IReadOnlyList<AccountPaymentAllocationDto> Scope);

public sealed record AccountEqualShareSlotSummaryDto(
    int Ordinal,
    long AmountMinor,
    AccountPaymentState? ClaimState,
    bool IsAvailable);

public sealed record AccountPaymentAttemptSummaryDto(
    Guid? OperationId,
    AccountPaymentState State,
    int Version,
    PaymentMethod PaymentMethod,
    long AmountMinor,
    string Currency,
    DateTime? ReservationExpiresAt,
    Guid? EqualSharePlanId,
    int? EqualShareOrdinal,
    bool IsOwnOperation);

public sealed record AccountPaymentLimitsDto(int MaximumSelectedUnits, int MaximumEqualShares);

public sealed record AccountPaymentAccountDto(
    Guid ServiceSessionId,
    TableServiceSessionStatus Status,
    long AccountRevision,
    string Currency,
    long OutstandingMinor,
    long ReservedMinor,
    long AvailableMinor,
    long CapturedAccountPaymentMinor,
    IReadOnlyList<AccountPaymentAllocationDto> OutstandingAllocations,
    IReadOnlyList<AccountPaymentAllocationDto> AvailableAllocations,
    AccountPaymentEqualShareSummaryDto? ActiveEqualSharePlan,
    IReadOnlyList<AccountPaymentAttemptSummaryDto> ActiveAttempts,
    AccountPaymentLimitsDto Limits);
