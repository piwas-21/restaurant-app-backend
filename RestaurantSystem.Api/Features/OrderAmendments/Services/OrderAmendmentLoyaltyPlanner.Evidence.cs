using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentLoyaltyPlanner
{
    private static OrderBillingSnapshotUnit[] SelectRemovedUnits(
        Order source, IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        IReadOnlyList<OrderBillingSnapshotUnit> units)
    {
        var ranges = changes.Where(value => value.Kind is OrderAmendmentChangeKind.Void
            or OrderAmendmentChangeKind.Replace).OrderBy(value => value.OrderItemId)
            .ThenBy(value => value.StartOrdinal).ToArray();
        var items = source.Items.Where(value => !value.ParentOrderItemId.HasValue)
            .ToDictionary(value => value.Id);
        var selected = new List<OrderBillingSnapshotUnit>();
        var identities = new HashSet<(Guid ItemId, int Ordinal)>();
        foreach (var change in ranges)
        {
            if (change.OrderItemId == Guid.Empty || change.Previous.Id != change.OrderItemId
                || change.StartOrdinal < 1 || change.Quantity < 1
                || !items.TryGetValue(change.OrderItemId, out var item)
                || (long)change.StartOrdinal + change.Quantity > (long)item.Quantity + 1)
                throw Held("A committed removal does not match the accepted root-item scope.");
            var end = (long)change.StartOrdinal + change.Quantity;
            var exact = units.Where(value => value.OrderItemId == change.OrderItemId
                && value.UnitOrdinal >= change.StartOrdinal && value.UnitOrdinal < end).ToArray();
            if (exact.Length != change.Quantity || exact.Any(value =>
                    !identities.Add((value.OrderItemId, value.UnitOrdinal))))
                throw Held("A committed removal has overlapping or incomplete frozen unit coverage.");
            selected.AddRange(exact);
        }
        return selected.OrderBy(value => value.OrderItemId).ThenBy(value => value.UnitOrdinal).ToArray();
    }

    private static SuppressionValidation ValidateSuppressionEvidence(
        Order source, Guid currentAmendmentId, IReadOnlyList<OrderBillingSnapshotUnit> units,
        OrderAmendmentLoyaltyEvidence evidence, IReadOnlyList<OrderBillingSnapshotUnit> removed,
        bool awardMissing)
    {
        var sourceOrderId = source.Id;
        var unitsById = units.ToDictionary(value => value.Id);
        var amendments = evidence.CommittedAmendments.ToDictionary(value => value.Id);
        var removedByAmendment = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var amendment in evidence.CommittedAmendments)
        {
            if (amendment.SourceOrderId != sourceOrderId || amendment.State != OrderAmendmentState.Committed)
                throw Held("The committed amendment history has a foreign or invalid source.");
            var priorChanges = amendment.Id == currentAmendmentId
                ? null : ReadChanges(amendment.ChangesJson);
            if (priorChanges is null)
                continue;
            removedByAmendment.Add(amendment.Id, SelectRemovedUnits(source, priorChanges, units)
                .Select(value => value.Id).ToHashSet());
        }

        var priorRemoved = removedByAmendment.Values.SelectMany(value => value).ToHashSet();
        if (removed.Any(value => priorRemoved.Contains(value.Id)))
            throw Held("A previously amended loyalty unit cannot be compensated twice.");

        var suppressionByUnit = new Dictionary<Guid, OrderBillingUnitAwardSuppression>();
        foreach (var suppression in evidence.Suppressions)
        {
            if (suppression.OrderId != sourceOrderId || suppression.SuppressedEarnedPoints <= 0
                || !unitsById.TryGetValue(suppression.SnapshotUnitId, out var unit)
                || unit.EarnedPoints != suppression.SuppressedEarnedPoints
                || !amendments.ContainsKey(suppression.AmendmentId)
                || !suppressionByUnit.TryAdd(suppression.SnapshotUnitId, suppression))
                throw Held("The frozen pre-award suppression history contains invalid or duplicate rows.");
            var scope = suppression.AmendmentId == currentAmendmentId
                ? removed.Select(value => value.Id).ToHashSet()
                : removedByAmendment.GetValueOrDefault(suppression.AmendmentId);
            if (scope is null || !scope.Contains(unit.Id))
                throw Held("A pre-award suppression does not match its committed removal.");
        }
        if (evidence.Suppressions.Sum(value => (long)value.SuppressedEarnedPoints)
            > units.Sum(value => (long)value.EarnedPoints))
            throw Held("The pre-award suppression history exceeds the frozen earning candidate.");

        var pendingSuppressions = new List<OrderAmendmentLoyaltyUnitAllocation>();
        if (awardMissing)
        {
            foreach (var unit in removed.Where(value => value.EarnedPoints > 0))
            {
                if (!suppressionByUnit.ContainsKey(unit.Id))
                    pendingSuppressions.Add(new(unit.Id, unit.OrderItemId, unit.UnitOrdinal,
                        unit.EarnedPoints, unit.RedeemedPoints));
            }
        }
        return new(pendingSuppressions, priorRemoved);
    }

    private static AwardValidation ValidateAward(
        Order source, AccountMoney money, AcceptedLoyaltySnapshot accepted,
        OrderAmendmentLoyaltyEvidence evidence, IReadOnlyList<OrderBillingSnapshotUnit> removed)
    {
        var candidate = accepted.Snapshot.EarnedPointsCandidate;
        var earnedRows = evidence.Transactions.Where(value => value.TransactionType == TransactionType.Earned).ToArray();
        if (candidate is null)
        {
            if (earnedRows.Length > 0 || evidence.AwardWitness is not null)
                throw Held("An unevaluated earning snapshot conflicts with applied award history.");
            return new(0, false, null, null, []);
        }
        if (earnedRows.Length > 1)
            throw Held("The source order has duplicate earned loyalty transactions.");
        if (evidence.AwardWitness is null)
        {
            if (earnedRows.Length > 0)
                throw Held("A legacy earned transaction with removals has no safe immutable award coverage.");
            return new(0, candidate.Value > 0, null, null, []);
        }

        var witness = evidence.AwardWitness;
        if (accepted.EarningOwnerLink is null || witness.OrderId != source.Id
            || witness.OwnerLinkId != accepted.EarningOwnerLink.Id
            || witness.CandidatePoints != candidate.Value || witness.AppliedPoints < 0
            || witness.SuppressedPoints < 0
            || (long)witness.AppliedPoints + witness.SuppressedPoints != candidate.Value
            || evidence.Suppressions.Sum(value => (long)value.SuppressedEarnedPoints) != witness.SuppressedPoints)
            throw Held("The immutable award witness does not match the accepted unit and suppression history.");

        var expectedOutcome = witness.AppliedPoints > 0
            ? OrderBillingAwardOutcome.Awarded
            : candidate == 0 ? OrderBillingAwardOutcome.EvaluatedZero : OrderBillingAwardOutcome.FullySuppressed;
        if (witness.Outcome != expectedOutcome)
            throw Held("The loyalty award witness has an invalid evaluated outcome.");
        if (witness.AppliedPoints == 0)
        {
            if (witness.EarnedTransactionId.HasValue || earnedRows.Length > 0
                || evidence.AwardCoverage.Count > 0)
                throw Held("A zero-point award witness conflicts with earned ledger history.");
            return new(0, false, null, witness, []);
        }

        if (!witness.EarnedTransactionId.HasValue || earnedRows.Length != 1
            || earnedRows[0].Id != witness.EarnedTransactionId.Value
            || !MatchesEarned(earnedRows[0], source, accepted.EarningOwnerLink.UserId!.Value,
                witness.AppliedPoints, money.ToMajor(accepted.Snapshot.EarningBasisMinor)))
            throw Held("The applied award does not match its unique immutable earned transaction.");

        var unitIds = accepted.Units.ToDictionary(value => value.Id);
        var coverage = evidence.AwardCoverage;
        if (coverage.Any(value => value.OrderId != source.Id || value.AwardWitnessId != witness.Id
                || value.EligibleEarnedPoints <= 0
                || !unitIds.TryGetValue(value.SnapshotUnitId, out var unit)
                || unit.EarnedPoints != value.EligibleEarnedPoints)
            || coverage.Select(value => value.SnapshotUnitId).Distinct().Count() != coverage.Count
            || coverage.Sum(value => (long)value.EligibleEarnedPoints) != witness.AppliedPoints)
            throw Held("The applied award unit coverage is invalid or incomplete.");
        if (coverage.Count == 0)
            throw Held("A historical award has no exact per-unit compensation basis.");
        return new(witness.AppliedPoints > 0 ? witness.AppliedPoints : 0,
            false, earnedRows[0], witness, coverage);
    }

    private static RedemptionValidation ValidateRedemption(
        Order source, AcceptedLoyaltySnapshot accepted, OrderAmendmentLoyaltyEvidence evidence)
    {
        var rows = evidence.Transactions.Where(value => value.TransactionType == TransactionType.Redeemed).ToArray();
        if (accepted.Snapshot.RedemptionTransactionId is null)
        {
            if (rows.Length > 0)
                throw Held("A redemption ledger row exists without a frozen accepted debit.");
            return new(null);
        }

        var id = accepted.Snapshot.RedemptionTransactionId.Value;
        if (accepted.RedemptionOwnerLink is null || rows.Length != 1 || rows[0].Id != id
            || rows[0].OrderId != source.Id || rows[0].UserId != accepted.RedemptionOwnerLink.UserId
            || rows[0].TransactionType != TransactionType.Redeemed
            || rows[0].Points != accepted.Snapshot.RedemptionTransactionPoints
            || rows[0].Points >= 0 || rows[0].OrderTotal != accepted.Snapshot.RedemptionTransactionOrderTotal
            || rows[0].CreatedAt != accepted.Snapshot.RedemptionTransactionCreatedAt)
            throw Held("The accepted points redemption does not match its exact persisted negative transaction.");
        return new(rows[0]);
    }

    private static List<OrderAmendmentChangeSnapshot> ReadChanges(string json)
    {
        try
        {
            var changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(json);
            if (changes.Any(value => value is null))
                throw Held("A committed amendment has incomplete loyalty scope evidence.");
            return changes;
        }
        catch (JsonException exception)
        {
            throw Held("A committed amendment has invalid loyalty scope evidence.", exception);
        }
    }

    private static bool MatchesEarned(FidelityPointsTransaction value, Order source,
        Guid ownerId, int points, decimal orderTotal) => value.Id != Guid.Empty
        && value.UserId == ownerId && value.OrderId == source.Id
        && value.TransactionType == TransactionType.Earned && value.Points == points
        && value.OrderTotal == orderTotal;

    private sealed record AwardValidation(
        int AppliedPoints, bool Pending, FidelityPointsTransaction? Transaction,
        OrderBillingAwardWitness? Witness, IReadOnlyList<OrderBillingAwardUnitCoverage> Coverage);

    private sealed record RedemptionValidation(FidelityPointsTransaction? Transaction);
    private sealed record SuppressionValidation(
        IReadOnlyList<OrderAmendmentLoyaltyUnitAllocation> PendingAwardSuppressions,
        IReadOnlySet<Guid> PriorRemovedUnitIds);
}
