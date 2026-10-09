using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed record TableBillPaymentFlow(string Mode, int? GuestCount, IReadOnlyList<TableBillGuestAmountDto> Guests);

internal static class TableBillPaymentFlowReader
{
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

        var capturedAttempts = context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => sessionIds.Contains(value.ServiceSessionId)
                && value.State == AccountPaymentState.Captured);
        var latestAttemptTimes = capturedAttempts
            .GroupBy(value => value.ServiceSessionId)
            .Select(group => new { SessionId = group.Key, CreatedAt = group.Max(value => value.CreatedAt) });
        var attemptCandidates = await (
            from attempt in capturedAttempts
            join attemptTime in latestAttemptTimes on new { SessionId = attempt.ServiceSessionId, attempt.CreatedAt }
                equals new { attemptTime.SessionId, attemptTime.CreatedAt }
            select new FlowCandidate(
                attempt.ServiceSessionId, attempt.CreatedAt, MapMode(attempt.Mode),
                attempt.EqualSharePlanId, attempt.EqualShareOrdinal))
            .ToListAsync(cancellationToken);

        var tablePayments = context.TableBillPaymentOperations.AsNoTracking()
            .Where(value => value.ServiceSessionId.HasValue && sessionIds.Contains(value.ServiceSessionId.Value));
        var latestTablePaymentTimes = tablePayments
            .GroupBy(value => value.ServiceSessionId!.Value)
            .Select(group => new { SessionId = group.Key, CreatedAt = group.Max(value => value.CreatedAt) });
        var tablePaymentCandidates = await (
            from payment in tablePayments
            join paymentTime in latestTablePaymentTimes on new
            { SessionId = payment.ServiceSessionId!.Value, payment.CreatedAt }
                equals new { paymentTime.SessionId, paymentTime.CreatedAt }
            select new FlowCandidate(payment.ServiceSessionId!.Value, payment.CreatedAt, "Amount", null, null))
            .ToListAsync(cancellationToken);

        var latestBySession = attemptCandidates.Concat(tablePaymentCandidates)
            .GroupBy(value => value.SessionId)
            .ToDictionary(group => group.Key,
                group => group.OrderByDescending(value => value.CreatedAt).ThenBy(value => value.PlanId).First());
        var planIds = latestBySession.Values.Where(value => value.PlanId.HasValue)
            .Select(value => value.PlanId!.Value).Distinct().ToArray();
        if (planIds.Length == 0)
            return latestBySession.ToDictionary(pair => pair.Key,
                pair => new TableBillPaymentFlow(pair.Value.Mode, null, Array.Empty<TableBillGuestAmountDto>()));

        var plans = await context.AccountEqualSharePlans.AsNoTracking()
            .Where(value => planIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var claims = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.EqualSharePlanId.HasValue && planIds.Contains(value.EqualSharePlanId.Value)
                && value.EqualShareOrdinal.HasValue
                && (value.State == AccountPaymentState.Captured || HoldingStates.Contains(value.State)))
            .Select(value => new { PlanId = value.EqualSharePlanId!.Value, Ordinal = value.EqualShareOrdinal!.Value, value.State })
            .ToListAsync(cancellationToken);
        var claimsByPlan = claims.GroupBy(value => value.PlanId)
            .ToDictionary(group => group.Key, group => group.ToDictionary(value => value.Ordinal, value => value.State));

        var result = new Dictionary<Guid, TableBillPaymentFlow>();
        foreach (var (sessionId, candidate) in latestBySession)
        {
            if (candidate.PlanId is not Guid planId || !plans.TryGetValue(planId, out var plan))
            {
                result[sessionId] = new TableBillPaymentFlow(candidate.Mode, null, Array.Empty<TableBillGuestAmountDto>());
                continue;
            }

            IReadOnlyList<AccountDebtSegment> scope;
            try
            {
                scope = AccountPaymentSnapshots.ReadScope(plan.ScopeJson);
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or BadRequestException)
            {
                throw new ConflictException("The table bill split plan requires reconciliation.", exception);
            }
            var customAmounts = plan.CustomAmountsJson is null
                ? null
                : AccountPaymentSnapshots.Deserialize<List<long>>(plan.CustomAmountsJson);
            if (plan.ShareCount < 2 || customAmounts is not null && customAmounts.Count != plan.ShareCount)
                throw new ConflictException("The table bill split plan requires reconciliation.");
            claimsByPlan.TryGetValue(planId, out var planClaims);
            var guests = Enumerable.Range(1, plan.ShareCount).Select(ordinal =>
            {
                var amount = customAmounts is null
                    ? AccountDebtMath.Total(AccountEqualScopeMath.ForShare(scope, plan.ShareCount, ordinal))
                    : customAmounts[ordinal - 1];
                var status = planClaims is not null && planClaims.TryGetValue(ordinal, out var state)
                    ? state == AccountPaymentState.Captured ? "Captured" : "Reserved"
                    : "Due";
                return new TableBillGuestAmountDto(ordinal, amount / 100m, status);
            }).ToArray();
            result[sessionId] = new TableBillPaymentFlow(candidate.Mode, plan.ShareCount, guests);
        }
        return result;
    }

    internal static async Task<TableBillPaymentFlow?> ReadLegacyAsync(
        ApplicationDbContext context, int tableNumber, CancellationToken cancellationToken)
    {
        var payment = await context.TableBillPaymentOperations.AsNoTracking()
            .Where(value => value.ServiceSessionId == null && value.TableNumber == tableNumber)
            .OrderByDescending(value => value.CreatedAt)
            .Select(value => new { value.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);
        return payment is null ? null
            : new TableBillPaymentFlow("Amount", null, Array.Empty<TableBillGuestAmountDto>());
    }

    private static string MapMode(AccountPaymentMode mode) => mode switch
    {
        AccountPaymentMode.Full => "Full",
        AccountPaymentMode.Amount => "Amount",
        AccountPaymentMode.Items => "ByItems",
        AccountPaymentMode.Equal => "Equal",
        AccountPaymentMode.CustomAmount => "CustomAmount",
        _ => "Amount"
    };

    private sealed record FlowCandidate(
        Guid SessionId, DateTime CreatedAt, string Mode, Guid? PlanId, int? Ordinal);
}
