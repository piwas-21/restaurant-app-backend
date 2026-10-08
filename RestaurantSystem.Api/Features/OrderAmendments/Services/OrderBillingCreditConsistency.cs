using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

/// <summary>Proves every materialized credit has the matching resolved amendment and journal row.</summary>
internal static class OrderBillingCreditConsistency
{
    internal static async Task AssertAsync(ApplicationDbContext context, IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0) return;
        var orders = await context.Orders.AsNoTracking().Where(value => orderIds.Contains(value.Id))
            .Select(value => new BillingCreditOrder(value.Id, value.BillingCreditAmount)).ToListAsync(cancellationToken);
        if (orders.Count != orderIds.Distinct().Count())
            throw InvalidJournal();
        var amendments = await context.OrderAmendments.AsNoTracking()
            .Where(value => orderIds.Contains(value.SourceOrderId) && value.State == OrderAmendmentState.Committed)
            .Select(value => new BillingCreditAmendment(value.Id, value.SourceOrderId, value.ActorUserId,
                value.ActorRole, value.FinancialResolutionJson)).ToListAsync(cancellationToken);
        var credits = await context.OrderBillingCredits.AsNoTracking()
            .Where(value => orderIds.Contains(value.SourceOrderId)).ToListAsync(cancellationToken);
        Validate(orders, amendments, credits);
    }

    internal static void Validate(IReadOnlyList<BillingCreditOrder> orders,
        IReadOnlyList<BillingCreditAmendment> amendments, IReadOnlyList<OrderBillingCredit> credits)
    {
        if (credits.Select(value => value.AmendmentId).Distinct().Count() != credits.Count
            || orders.Select(value => value.Id).Distinct().Count() != orders.Count
            || amendments.Select(value => value.Id).Distinct().Count() != amendments.Count)
            throw InvalidJournal();
        var creditAmendmentIds = credits.Select(value => value.AmendmentId).ToHashSet();
        var resolved = ReadResolvedOutcomes(amendments, creditAmendmentIds);
        if (resolved.Count != credits.Count)
            throw InvalidJournal();
        RequireMatchingCreditEntries(credits, resolved);
        RequireMatchingOrderTotals(orders, credits);
        if (credits.Any(value => orders.All(order => order.Id != value.SourceOrderId)))
            throw InvalidJournal();
    }

    private static Dictionary<Guid, (BillingCreditAmendment Amendment, OrderAmendmentFinancialPreviewDto Outcome)>
        ReadResolvedOutcomes(IReadOnlyList<BillingCreditAmendment> amendments,
            HashSet<Guid> creditAmendmentIds)
    {
        var resolved = new Dictionary<Guid, (BillingCreditAmendment Amendment, OrderAmendmentFinancialPreviewDto Outcome)>();
        foreach (var amendment in amendments)
        {
            var outcome = OrderAmendmentFinancialGuard.ReadValidSnapshot(amendment.FinancialResolutionJson);
            if (outcome.ResolutionStatus == OrderAmendmentFinancialResolutionStatus.Pending)
            {
                if (outcome.PotentialCreditMinor <= 0
                    || outcome.CreditState != OrderAmendmentCreditState.PendingAllocationReview
                    || creditAmendmentIds.Contains(amendment.Id))
                    throw InvalidJournal();
                continue;
            }
            if (outcome.PotentialCreditMinor > 0)
            {
                RequireResolvedCreditOutcome(outcome);
                resolved.Add(amendment.Id, (amendment, outcome));
            }
            else if (outcome.ResolutionStatus != OrderAmendmentFinancialResolutionStatus.NotRequired
                || outcome.CreditState != OrderAmendmentCreditState.None
                || outcome.LoyaltyState != OrderAmendmentLoyaltyState.None
                || outcome.RefundState != OrderAmendmentRefundState.None
                || creditAmendmentIds.Contains(amendment.Id))
                throw InvalidJournal();
        }
        return resolved;
    }

    private static void RequireResolvedCreditOutcome(OrderAmendmentFinancialPreviewDto outcome)
    {
        var appliedBalanceReduction = outcome.CreditState == OrderAmendmentCreditState.BalanceReduction
            && outcome.LoyaltyState == OrderAmendmentLoyaltyState.None
            && IsNoLoyaltyEffect(outcome.Loyalty)
            && outcome.RefundState == OrderAmendmentRefundState.None;
        var reconciledTenderCredit = outcome.CreditState == OrderAmendmentCreditState.Resolved
            && HasSettledLoyaltyEvidence(outcome)
            && outcome.RefundState == OrderAmendmentRefundState.Resolved;
        if (outcome.ResolutionStatus != OrderAmendmentFinancialResolutionStatus.Resolved
            || !appliedBalanceReduction && !reconciledTenderCredit)
            throw InvalidJournal();
    }

    private static bool HasSettledLoyaltyEvidence(OrderAmendmentFinancialPreviewDto outcome)
    {
        if (outcome.LoyaltyState == OrderAmendmentLoyaltyState.None)
            return IsNoLoyaltyEffect(outcome.Loyalty);
        var loyalty = outcome.Loyalty;
        return outcome.LoyaltyState == OrderAmendmentLoyaltyState.Resolved
            && loyalty is not null
            && loyalty.State == OrderAmendmentLoyaltyOperationStatus.Resolved
            && HasValidResolvedEarningEvidence(loyalty)
            && loyalty.EarnedClawbackPoints >= 0 && loyalty.RedemptionRestorationPoints >= 0
            && loyalty.PostedClawbackPoints == loyalty.EarnedClawbackPoints
            && loyalty.PostedRestorationPoints == loyalty.RedemptionRestorationPoints
            && (loyalty.EarnedClawbackPoints > 0 || loyalty.RedemptionRestorationPoints > 0
                || loyalty.SuppressedPoints > 0);
    }

    private static bool HasValidResolvedEarningEvidence(OrderAmendmentLoyaltyResultDto loyalty)
    {
        if (loyalty.EarningDisposition == OrderBillingEarningDisposition.Evaluated)
        {
            return !loyalty.EarningRetired && loyalty.CandidatePoints is int candidate && candidate >= 0
                && loyalty.AppliedAwardPoints >= 0 && loyalty.SuppressedPoints >= 0
                && (long)loyalty.AppliedAwardPoints + loyalty.SuppressedPoints <= candidate
                && (loyalty.AwardPending || (long)loyalty.AppliedAwardPoints + loyalty.SuppressedPoints == candidate);
        }

        return (loyalty.EarningDisposition is OrderBillingEarningDisposition.Unevaluated
                or OrderBillingEarningDisposition.NoCustomerOwnerAtAcceptance
                or OrderBillingEarningDisposition.LoyaltyModuleDisabledAtAcceptance)
            && !loyalty.CandidatePoints.HasValue && !loyalty.AwardPending
            && loyalty.AppliedAwardPoints == 0 && loyalty.SuppressedPoints == 0
            && loyalty.EarnedClawbackPoints == 0
            && (loyalty.EarningDisposition == OrderBillingEarningDisposition.Unevaluated
                ? loyalty.EarningRetired : !loyalty.EarningRetired);
    }

    private static bool IsNoLoyaltyEffect(OrderAmendmentLoyaltyResultDto? loyalty) => loyalty is null
        || loyalty.State == OrderAmendmentLoyaltyOperationStatus.None
            && loyalty.EarnedClawbackPoints == 0 && loyalty.RedemptionRestorationPoints == 0
            && loyalty.PostedClawbackPoints == 0 && loyalty.PostedRestorationPoints == 0
            && loyalty.ClawbackShortfallPoints is null;

    private static void RequireMatchingCreditEntries(
        IReadOnlyList<OrderBillingCredit> credits,
        Dictionary<Guid, (BillingCreditAmendment Amendment, OrderAmendmentFinancialPreviewDto Outcome)> resolved)
    {
        foreach (var credit in credits)
        {
            if (!resolved.TryGetValue(credit.AmendmentId, out var evidence)
                || credit.SourceOrderId != evidence.Amendment.SourceOrderId
                || credit.ActorUserId != evidence.Amendment.ActorUserId
                || credit.ActorRole != evidence.Amendment.ActorRole
                || credit.AmountMinor <= 0 || credit.AmountMinor != evidence.Outcome.PotentialCreditMinor
                || credit.Currency != evidence.Outcome.Currency
                || credit.Currency != new AccountMoney(credit.Currency).Currency)
                throw InvalidJournal();
        }
    }

    private static void RequireMatchingOrderTotals(
        IReadOnlyList<BillingCreditOrder> orders, IReadOnlyList<OrderBillingCredit> credits)
    {
        foreach (var order in orders)
        {
            var entries = credits.Where(value => value.SourceOrderId == order.Id).ToArray();
            if (entries.Length == 0)
            {
                if (order.Amount != 0) throw InvalidJournal();
                continue;
            }
            var money = new AccountMoney(entries[0].Currency);
            if (entries.Any(value => value.Currency != money.Currency)
                || money.ToMinor(order.Amount) != entries.Sum(value => value.AmountMinor))
                throw InvalidJournal();
        }
    }

    private static ConflictException InvalidJournal() => new(
        "The billing credit journal, amendment outcome, and order balance require reconciliation.");
}

internal sealed record BillingCreditOrder(Guid Id, decimal Amount);
internal sealed record BillingCreditAmendment(
    Guid Id, Guid SourceOrderId, Guid ActorUserId, string ActorRole, string FinancialResolutionJson);
