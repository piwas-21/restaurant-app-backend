using System.Text.Json;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentEarningRetirementRules
{
    internal static bool IsEligible(
        Order source, OrderAmendment amendment, IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        OrderAmendmentLoyaltyEvidence evidence, AccountMoney money,
        OrderAmendmentFinancialPreviewDto recalculatedFinancial, out int unitCount)
    {
        unitCount = 0;
        if (evidence.Retirement is not null || evidence.Operations.Count != 0
            || !HasPendingPaidCredit(amendment, recalculatedFinancial)
            || !OrderAmendmentLoyaltyPlanner.HasValidRetirementSnapshot(source, money, evidence)
            || !MatchesUnknownFullSourceVoid(source, amendment, changes, evidence, out unitCount))
        {
            unitCount = 0;
            return false;
        }
        return true;
    }

    internal static bool MatchesRetirement(
        Order source, OrderAmendment amendment, IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        OrderAmendmentLoyaltyEvidence evidence, AccountMoney money, out int unitCount)
    {
        unitCount = 0;
        var retirement = evidence.Retirement;
        if (retirement is null || retirement.OrderId != source.Id
            || retirement.SnapshotId != evidence.Snapshot?.Id || retirement.AmendmentId != amendment.Id
            || !OrderAmendmentLoyaltyPlanner.HasValidRetirementSnapshot(source, money, evidence)
            || !MatchesUnknownFullSourceVoid(source, amendment, changes, evidence, out unitCount)
            || retirement.RetiredUnitCount != unitCount)
        {
            unitCount = 0;
            return false;
        }
        return true;
    }

    internal static bool MatchesStoredRetirement(
        Order source, AccountMoney money, OrderAmendmentLoyaltyEvidence evidence, out int unitCount)
    {
        unitCount = 0;
        var retirement = evidence.Retirement;
        var amendment = evidence.CommittedAmendments.SingleOrDefault(value => value.Id == retirement?.AmendmentId);
        if (retirement is null || amendment is null)
            return false;
        try
        {
            var changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson);
            return MatchesRetirement(source, amendment, changes, evidence, money, out unitCount);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasPendingPaidCredit(
        OrderAmendment amendment, OrderAmendmentFinancialPreviewDto recalculatedFinancial)
    {
        try
        {
            var financial = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(
                amendment.FinancialResolutionJson);
            return financial == recalculatedFinancial
                && financial.ResolutionStatus == OrderAmendmentFinancialResolutionStatus.Pending
                && financial.CreditState == OrderAmendmentCreditState.PendingAllocationReview
                && financial.PotentialCreditMinor > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasPartialEarningFacts(OrderBillingSnapshot snapshot) =>
        snapshot.EarningEvaluationVersion is not null || snapshot.EarningRuleSetFingerprint is not null
        || snapshot.EarningRuleId.HasValue || snapshot.EarningRuleName is not null
        || snapshot.EarningRuleMinimumMinor.HasValue || snapshot.EarningRuleMaximumMinor.HasValue
        || snapshot.EarningRulePoints.HasValue || snapshot.EarningRulePriority.HasValue;
}
