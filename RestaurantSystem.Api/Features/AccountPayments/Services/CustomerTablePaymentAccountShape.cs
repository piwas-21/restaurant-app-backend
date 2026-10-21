using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Schema1 JSON bounds, independent of tenant display settings and database scan size.</summary>
internal static class CustomerTablePaymentAccountShape
{
    private const long MaximumExactInteger = 9_007_199_254_740_991;
    private const int MaximumSegments = 5_000;
    private const int MaximumAttempts = 1_000;
    private const int MaximumShares = 1_000;

    public static void Require(AccountPaymentAccountDto account, Guid serviceSessionId, string currency)
    {
        if (account.ServiceSessionId != serviceSessionId || account.Status != TableServiceSessionStatus.Open
            || account.Currency != currency || !Exact(account.AccountRevision, positive: true)
            || !Exact(account.OutstandingMinor) || !Exact(account.ReservedMinor) || !Exact(account.AvailableMinor)
            || !Exact(account.CapturedAccountPaymentMinor) || account.BillingAllocationVersion is < 0 or > 1
            || account.Limits.MaximumSelectedUnits is < 1 or > MaximumShares
            || account.Limits.MaximumEqualShares is < 1 or > MaximumShares
            || account.ActiveAttempts.Count > MaximumAttempts) throw Unsupported();
        RequireAllocations(account.OutstandingAllocations);
        RequireAllocations(account.AvailableAllocations);
        foreach (var attempt in account.ActiveAttempts)
            if (!Enum.IsDefined(attempt.State) || !Enum.IsDefined(attempt.PaymentMethod)
                || attempt.Version <= 0 || !Exact(attempt.AmountMinor, positive: true) || attempt.Currency != currency
                || attempt.IsOwnOperation != attempt.OperationId.HasValue || attempt.OperationId == Guid.Empty)
                throw Unsupported();
        if (account.Limits.Online is { } limits && (limits.Currency != currency
                || !Exact(limits.MinimumAmountMinor, positive: true) || !Exact(limits.MaximumAmountMinor, positive: true)
                || limits.MinimumAmountMinor > limits.MaximumAmountMinor)) throw Unsupported();
        if (account.ActiveEqualSharePlan is { } plan) RequirePlan(plan, currency);
    }

    private static void RequirePlan(AccountPaymentEqualShareSummaryDto plan, string currency)
    {
        if (plan.PlanId == Guid.Empty || !Exact(plan.AccountRevision, positive: true)
            || !Exact(plan.TotalMinor, positive: true) || plan.Currency != currency
            || plan.ShareCount is < 2 or > MaximumShares || plan.Slots.Count != plan.ShareCount
            || plan.Slots.Select(slot => slot.Ordinal).Distinct().Count() != plan.ShareCount
            || plan.Slots.Any(slot => slot.Ordinal < 1 || slot.Ordinal > plan.ShareCount
                || !Exact(slot.AmountMinor, positive: true) || slot.ClaimState.HasValue && !Enum.IsDefined(slot.ClaimState.Value)))
            throw Unsupported();
        RequireAllocations(plan.Scope);
        if (plan.CustomAmountsMinor is { } amounts && (amounts.Count != plan.ShareCount
            || amounts.Any(amount => !Exact(amount, positive: true)))) throw Unsupported();
    }

    private static void RequireAllocations(IReadOnlyList<AccountPaymentAllocationDto> rows)
    {
        if (rows.Count > MaximumSegments || rows.Any(row => row.OrderId == Guid.Empty || row.OrderItemId == Guid.Empty
            || row.StartOrdinal < 1 || row.UnitCount < 1 || !Exact(row.MinorPerUnit, positive: true)
            || !Exact(row.AmountMinor, positive: true))) throw Unsupported();
    }

    private static bool Exact(long value, bool positive = false) => value >= (positive ? 1 : 0) && value <= MaximumExactInteger;
    private static ConflictException Unsupported() => new("The table payment account cannot be displayed safely. Ask staff for the account.");
}
