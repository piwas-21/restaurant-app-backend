using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed record TableBillPaymentFlow(string Mode, int? GuestCount, IReadOnlyList<TableBillGuestAmountDto> Guests);

internal static class TableBillPaymentFlowReader
{
    private const string AmountMode = "Amount";
    private const string EqualMode = "Equal";
    private const string CustomAmountMode = "CustomAmount";
    private const int MinorUnitsPerCurrencyUnit = 100;

    private static readonly AccountPaymentState[] HoldingStates =
    [
        AccountPaymentState.Reserved,
        AccountPaymentState.Starting,
        AccountPaymentState.Processing,
        AccountPaymentState.CancelRequested,
        AccountPaymentState.ReconciliationRequired
    ];

    internal static async Task<IReadOnlyDictionary<Guid, TableBillPaymentFlow>> ReadManyAsync(
        ApplicationDbContext context, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        if (sessionIds.Count == 0) return new Dictionary<Guid, TableBillPaymentFlow>();

        var candidates = await ReadCapturedAttemptCandidatesAsync(context, sessionIds, cancellationToken);
        candidates.AddRange(await ReadTablePaymentCandidatesAsync(context, sessionIds, cancellationToken));
        candidates.AddRange(await ReadActivePlanCandidatesAsync(context, sessionIds, cancellationToken));
        var latest = ChooseLatest(candidates);
        return await ReadFlowsAsync(context, latest, cancellationToken);
    }

    internal static async Task<TableBillPaymentFlow?> ReadLegacyAsync(
        ApplicationDbContext context, int tableNumber, CancellationToken cancellationToken)
    {
        var hasPayment = await context.TableBillPaymentOperations.AsNoTracking()
            .AnyAsync(value => value.ServiceSessionId == null && value.TableNumber == tableNumber, cancellationToken);
        return hasPayment
            ? new TableBillPaymentFlow(AmountMode, null, Array.Empty<TableBillGuestAmountDto>())
            : null;
    }

    private static async Task<List<FlowCandidate>> ReadCapturedAttemptCandidatesAsync(
        ApplicationDbContext context, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        var capturedAttempts = context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => sessionIds.Contains(value.ServiceSessionId)
                && value.State == AccountPaymentState.Captured);
        var latestTimes = capturedAttempts
            .GroupBy(value => value.ServiceSessionId)
            .Select(group => new { SessionId = group.Key, CreatedAt = group.Max(value => value.CreatedAt) });
        return await (
            from attempt in capturedAttempts
            join attemptTime in latestTimes on new { SessionId = attempt.ServiceSessionId, attempt.CreatedAt }
                equals new { attemptTime.SessionId, attemptTime.CreatedAt }
            select new FlowCandidate(
                attempt.ServiceSessionId, attempt.CreatedAt, true, attempt.Id, MapMode(attempt.Mode),
                attempt.EqualSharePlanId, attempt.EqualShareOrdinal))
            .ToListAsync(cancellationToken);
    }

    private static async Task<List<FlowCandidate>> ReadTablePaymentCandidatesAsync(
        ApplicationDbContext context, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        var payments = context.TableBillPaymentOperations.AsNoTracking()
            .Where(value => value.ServiceSessionId.HasValue && sessionIds.Contains(value.ServiceSessionId.Value));
        var latestTimes = payments
            .GroupBy(value => value.ServiceSessionId!.Value)
            .Select(group => new { SessionId = group.Key, CreatedAt = group.Max(value => value.CreatedAt) });
        return await (
            from payment in payments
            join paymentTime in latestTimes on new
            { SessionId = payment.ServiceSessionId!.Value, payment.CreatedAt }
                equals new { paymentTime.SessionId, paymentTime.CreatedAt }
            select new FlowCandidate(payment.ServiceSessionId!.Value, payment.CreatedAt, true, payment.Id,
                AmountMode, null, null))
            .ToListAsync(cancellationToken);
    }

    private static async Task<List<FlowCandidate>> ReadActivePlanCandidatesAsync(
        ApplicationDbContext context, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        var candidates = await context.AccountEqualSharePlans.AsNoTracking()
            .Where(value => sessionIds.Contains(value.ServiceSessionId) && value.InvalidatedAt == null)
            .Select(value => new FlowCandidate(value.ServiceSessionId, value.CreatedAt, false, value.Id,
                value.CustomAmountsJson == null ? EqualMode : CustomAmountMode, value.Id, null))
            .ToListAsync(cancellationToken);
        if (candidates.GroupBy(value => value.SessionId).Any(group => group.Count() > 1))
            throw new ConflictException("The table bill has multiple active split plans to reconcile.");
        return candidates;
    }

    private static Dictionary<Guid, FlowCandidate> ChooseLatest(IEnumerable<FlowCandidate> candidates) =>
        candidates.GroupBy(value => value.SessionId)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(value => value.CreatedAt)
                .ThenByDescending(value => value.IsCaptured)
                .ThenBy(value => value.CandidateId)
                .First());

    private static async Task<IReadOnlyDictionary<Guid, TableBillPaymentFlow>> ReadFlowsAsync(
        ApplicationDbContext context, Dictionary<Guid, FlowCandidate> latest, CancellationToken cancellationToken)
    {
        var planIds = latest.Values.Where(value => value.PlanId.HasValue)
            .Select(value => value.PlanId!.Value).Distinct().ToArray();
        if (planIds.Length == 0)
            return latest.ToDictionary(pair => pair.Key, pair => EmptyFlow(pair.Value.Mode));

        var plans = await context.AccountEqualSharePlans.AsNoTracking()
            .Where(value => planIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var claims = await ReadPlanClaimsAsync(context, planIds, cancellationToken);
        return latest.ToDictionary(pair => pair.Key,
            pair => BuildFlow(pair.Value, plans, claims));
    }

    private static async Task<Dictionary<Guid, Dictionary<int, AccountPaymentState>>> ReadPlanClaimsAsync(
        ApplicationDbContext context, Guid[] planIds, CancellationToken cancellationToken)
    {
        var claims = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.EqualSharePlanId.HasValue && planIds.Contains(value.EqualSharePlanId.Value)
                && value.EqualShareOrdinal.HasValue
                && (value.State == AccountPaymentState.Captured || HoldingStates.Contains(value.State)))
            .Select(value => new
            {
                PlanId = value.EqualSharePlanId!.Value,
                Ordinal = value.EqualShareOrdinal!.Value,
                value.State
            })
            .ToListAsync(cancellationToken);
        return claims.GroupBy(value => value.PlanId)
            .ToDictionary(group => group.Key,
                group => group.ToDictionary(value => value.Ordinal, value => value.State));
    }

    private static TableBillPaymentFlow BuildFlow(
        FlowCandidate candidate,
        Dictionary<Guid, AccountEqualSharePlan> plans,
        Dictionary<Guid, Dictionary<int, AccountPaymentState>> claims)
    {
        if (candidate.PlanId is not Guid planId || !plans.TryGetValue(planId, out var plan))
            return EmptyFlow(candidate.Mode);

        var customAmounts = ReadCustomAmounts(plan);
        var scope = ReadPlanScope(plan);
        ValidatePlan(plan, scope, customAmounts);
        claims.TryGetValue(planId, out var planClaims);
        var guests = Enumerable.Range(1, plan.ShareCount)
            .Select(ordinal => BuildGuestAmount(plan, scope, customAmounts, planClaims, ordinal))
            .ToArray();
        return new TableBillPaymentFlow(candidate.Mode, plan.ShareCount, guests);
    }

    private static List<long>? ReadCustomAmounts(AccountEqualSharePlan plan)
    {
        try
        {
            return plan.CustomAmountsJson is null
                ? null
                : AccountPaymentSnapshots.Deserialize<List<long>>(plan.CustomAmountsJson);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or BadRequestException)
        {
            throw new ConflictException("The table bill split plan requires reconciliation.", exception);
        }
    }

    private static IReadOnlyList<AccountDebtSegment> ReadPlanScope(AccountEqualSharePlan plan)
    {
        try
        {
            return AccountPaymentSnapshots.ReadScope(plan.ScopeJson);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or BadRequestException)
        {
            throw new ConflictException("The table bill split plan requires reconciliation.", exception);
        }
    }

    private static void ValidatePlan(
        AccountEqualSharePlan plan,
        IReadOnlyList<AccountDebtSegment> scope,
        List<long>? customAmounts)
    {
        long scopeTotal;
        long customTotal;
        try
        {
            scopeTotal = AccountDebtMath.Total(scope);
            customTotal = customAmounts?.Aggregate(0L, (sum, amount) => checked(sum + amount)) ?? plan.TotalMinor;
        }
        catch (OverflowException exception)
        {
            throw new ConflictException("The table bill split plan requires reconciliation.", exception);
        }

        var invalidCustomAmounts = customAmounts is not null
            && (customAmounts.Count != plan.ShareCount || customAmounts.Any(value => value <= 0)
                || customTotal != plan.TotalMinor);
        if (plan.ShareCount < 2 || scopeTotal != plan.TotalMinor
            || (customAmounts is null && plan.TotalMinor < plan.ShareCount) || invalidCustomAmounts)
            throw new ConflictException("The table bill split plan requires reconciliation.");
    }

    private static TableBillGuestAmountDto BuildGuestAmount(
        AccountEqualSharePlan plan,
        IReadOnlyList<AccountDebtSegment> scope,
        List<long>? customAmounts,
        IReadOnlyDictionary<int, AccountPaymentState>? claims,
        int ordinal)
    {
        var amountMinor = customAmounts is null
            ? AccountDebtMath.Total(AccountEqualScopeMath.ForShare(
                scope, plan.ShareCount, ordinal))
            : customAmounts[ordinal - 1];
        var status = ResolveGuestStatus(claims, ordinal);
        return new TableBillGuestAmountDto(ordinal, amountMinor / (decimal)MinorUnitsPerCurrencyUnit, status);
    }

    private static string ResolveGuestStatus(
        IReadOnlyDictionary<int, AccountPaymentState>? claims, int ordinal)
    {
        if (claims is null || !claims.TryGetValue(ordinal, out var state)) return "Due";
        return state == AccountPaymentState.Captured ? "Captured" : "Reserved";
    }

    private static TableBillPaymentFlow EmptyFlow(string mode) =>
        new(mode, null, Array.Empty<TableBillGuestAmountDto>());

    private static string MapMode(AccountPaymentMode mode) => mode switch
    {
        AccountPaymentMode.Full => "Full",
        AccountPaymentMode.Amount => AmountMode,
        AccountPaymentMode.Items => "ByItems",
        AccountPaymentMode.Equal => EqualMode,
        AccountPaymentMode.CustomAmount => CustomAmountMode,
        _ => AmountMode
    };

    private sealed record FlowCandidate(
        Guid SessionId,
        DateTime CreatedAt,
        bool IsCaptured,
        Guid CandidateId,
        string Mode,
        Guid? PlanId,
        int? Ordinal);
}
