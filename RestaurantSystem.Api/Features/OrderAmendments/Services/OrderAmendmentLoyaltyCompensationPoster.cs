using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentLoyaltyCompensationPoster
{
    private sealed record CompensationToPost(
        OrderAmendmentLoyaltyCompensation Header,
        OrderAmendmentLoyaltyCompensationPlan Plan,
        FidelityPointsTransaction Original,
        Guid UserId);

    private sealed record ValidatedCompensationPlan(
        IReadOnlyList<CompensationToPost> Matches,
        Guid? OwnerId,
        long TotalClawback,
        long TotalRestoration);

    private sealed record PostingInputs(
        ApplicationDbContext Context,
        OrderAmendmentResolutionOperation Operation,
        OrderAmendmentLoyaltyCompensation Header,
        OrderAmendmentLoyaltyCompensationPlan Plan,
        FidelityPointsTransaction Original,
        Guid UserId,
        DateTime Now,
        string Audit);

    internal static async Task ApplyAsync(ApplicationDbContext context,
        OrderAmendmentResolutionOperation operation, OrderAmendmentLoyaltyPlan? plan,
        OrderAmendmentLoyaltyEvidence evidence, DateTime now, string audit,
        CancellationToken cancellationToken)
    {
        if (plan is null || plan.SnapshotId is null)
        {
            await EnsureNoUnexpectedCompensationAsync(context, operation, evidence, cancellationToken);
            return;
        }

        var lockedOwnerLinks = await OrderAmendmentLoyaltyOwnerLinks.LockForOrderAsync(
            context, operation.SourceOrderId, cancellationToken);
        var rows = await context.OrderAmendmentLoyaltyCompensations
            .Where(value => value.OperationId == operation.Id).ToArrayAsync(cancellationToken);
        if (rows.Length != plan.Compensations.Count)
            throw Invalid();
        var validated = await ValidatePlanAsync(
            context, operation, plan, evidence, lockedOwnerLinks, rows, cancellationToken);
        if (validated.TotalClawback == 0 && validated.TotalRestoration == 0)
            return;
        var headerIds = rows.Select(value => value.Id).ToArray();
        if (await context.OrderAmendmentLoyaltyCompensationPostings.AsNoTracking()
            .AnyAsync(value => headerIds.Contains(value.CompensationId), cancellationToken))
            throw Invalid();
        var userId = validated.OwnerId ?? throw Invalid();
        if (!await OrderAmendmentLoyaltyBalanceLocks.LockUserAsync(
                context, userId, cancellationToken))
            throw new ConflictException("The source loyalty owner is unavailable for exact compensation.");
        await PostValidatedCompensationsAsync(
            context, operation, validated, now, audit, cancellationToken);
    }

    private static async Task EnsureNoUnexpectedCompensationAsync(
        ApplicationDbContext context, OrderAmendmentResolutionOperation operation,
        OrderAmendmentLoyaltyEvidence evidence, CancellationToken cancellationToken)
    {
        if (await context.OrderAmendmentLoyaltyCompensations.AnyAsync(
                value => value.OperationId == operation.Id, cancellationToken)
            || evidence.Suppressions.Any(value => value.AmendmentId == operation.AmendmentId))
            throw Invalid();
    }

    private static async Task<ValidatedCompensationPlan> ValidatePlanAsync(
        ApplicationDbContext context, OrderAmendmentResolutionOperation operation,
        OrderAmendmentLoyaltyPlan plan, OrderAmendmentLoyaltyEvidence evidence,
        IReadOnlyList<OrderBillingSnapshotOwnerLink> lockedOwnerLinks,
        IReadOnlyList<OrderAmendmentLoyaltyCompensation> rows, CancellationToken cancellationToken)
    {
        var ownerLinks = lockedOwnerLinks.ToDictionary(value => value.Id);
        var originals = evidence.Transactions.ToDictionary(value => value.Id);
        var units = evidence.Units.Select(value => value.Id).ToHashSet();
        var matches = new List<CompensationToPost>(plan.Compensations.Count);
        foreach (var item in plan.Compensations)
        {
            var header = FindHeader(rows, item, operation);
            var owner = FindAndValidateOwner(header, item, ownerLinks, evidence, operation);
            var original = FindAndValidateOriginal(header, originals, operation, owner.UserId!.Value);
            var storedUnits = await ReadStoredUnitsAsync(context, header.Id, cancellationToken);
            ValidateStoredPlanUnits(storedUnits, operation.SourceOrderId, item, units);
            matches.Add(new(header, item, original, owner.UserId.Value));
        }

        ValidateOwnerConsistency(matches);
        ValidateCurrentSuppressions(plan, operation, evidence);
        var clawback = SumRequired(matches, OrderAmendmentLoyaltyCompensationKind.EarnedClawback);
        var restoration = SumRequired(matches, OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration);
        return new(matches, matches.Select(value => value.UserId).Distinct().SingleOrDefault(), clawback, restoration);
    }

    private static OrderAmendmentLoyaltyCompensation FindHeader(
        IReadOnlyList<OrderAmendmentLoyaltyCompensation> rows,
        OrderAmendmentLoyaltyCompensationPlan item, OrderAmendmentResolutionOperation operation) =>
        rows.SingleOrDefault(value => value.AmendmentId == operation.AmendmentId
            && value.OriginalTransactionId == item.OriginalTransactionId && value.Kind == item.Kind)
        ?? throw Invalid();

    private static OrderBillingSnapshotOwnerLink FindAndValidateOwner(
        OrderAmendmentLoyaltyCompensation header, OrderAmendmentLoyaltyCompensationPlan item,
        Dictionary<Guid, OrderBillingSnapshotOwnerLink> ownerLinks,
        OrderAmendmentLoyaltyEvidence evidence, OrderAmendmentResolutionOperation operation)
    {
        if (header.SourceOrderId != operation.SourceOrderId || header.SnapshotId != item.SnapshotId
            || header.OwnerLinkId != item.OwnerLinkId || header.AwardWitnessId != item.AwardWitnessId
            || header.OperationId != operation.Id || header.OriginalTransactionPoints != item.OriginalTransactionPoints
            || header.RequiredPoints != item.RequiredPoints || header.PlanFingerprint != item.PlanFingerprint
            || !ownerLinks.TryGetValue(header.OwnerLinkId, out var owner)
            || !evidence.OwnerLinks.Any(value => value.Id == owner.Id
                && value.UserId == owner.UserId && value.Disposition == owner.Disposition)
            || owner.OrderId != operation.SourceOrderId
            || owner.Disposition != OrderBillingSnapshotOwnerDisposition.Linked
            || owner.UserId is null || owner.ErasedAt.HasValue || owner.ErasureTransactionId is not null)
            throw Invalid();
        return owner;
    }

    private static FidelityPointsTransaction FindAndValidateOriginal(
        OrderAmendmentLoyaltyCompensation header,
        Dictionary<Guid, FidelityPointsTransaction> originals,
        OrderAmendmentResolutionOperation operation, Guid userId)
    {
        if (!originals.TryGetValue(header.OriginalTransactionId, out var original)
            || original.OrderId != operation.SourceOrderId || original.UserId != userId
            || original.Points != header.OriginalTransactionPoints)
            throw Invalid();
        return original;
    }

    private static async Task<IReadOnlyList<OrderAmendmentLoyaltyCompensationUnit>> ReadStoredUnitsAsync(
        ApplicationDbContext context, Guid headerId, CancellationToken cancellationToken) =>
        await context.OrderAmendmentLoyaltyCompensationUnits.AsNoTracking()
            .Where(value => value.CompensationId == headerId).ToArrayAsync(cancellationToken);

    private static void ValidateStoredPlanUnits(
        IReadOnlyCollection<OrderAmendmentLoyaltyCompensationUnit> storedUnits,
        Guid sourceOrderId, OrderAmendmentLoyaltyCompensationPlan item, IReadOnlySet<Guid> unitIds)
    {
        if (!StoredUnitsMatch(storedUnits, sourceOrderId, item.Kind, item.Units, unitIds)
            || storedUnits.Sum(value => (long)value.Points) != item.RequiredPoints)
            throw Invalid();
    }

    private static void ValidateOwnerConsistency(IReadOnlyCollection<CompensationToPost> matches)
    {
        if (matches.Select(value => value.UserId).Distinct().Count() > 1)
            throw new ConflictException("The source order has conflicting immutable loyalty owners.");
    }

    private static void ValidateCurrentSuppressions(
        OrderAmendmentLoyaltyPlan plan, OrderAmendmentResolutionOperation operation,
        OrderAmendmentLoyaltyEvidence evidence)
    {
        var actual = evidence.Suppressions.Where(value => value.AmendmentId == operation.AmendmentId
                && value.OrderId == operation.SourceOrderId)
            .Select(value => value.SnapshotUnitId).Order().ToArray();
        var expected = plan.AwardPending
            ? plan.RemovedUnits.Where(value => value.EarnedPoints > 0)
                .Select(value => value.SnapshotUnitId).Order().ToArray()
            : [];
        if (!actual.SequenceEqual(expected))
            throw Invalid();
    }

    private static long SumRequired(
        IEnumerable<CompensationToPost> matches, OrderAmendmentLoyaltyCompensationKind kind) =>
        matches.Where(value => kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                ? value.Plan.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback
                : value.Plan.Kind != OrderAmendmentLoyaltyCompensationKind.EarnedClawback)
            .Sum(value => (long)value.Plan.RequiredPoints);

    private static async Task PostValidatedCompensationsAsync(
        ApplicationDbContext context, OrderAmendmentResolutionOperation operation,
        ValidatedCompensationPlan validated, DateTime now, string audit, CancellationToken cancellationToken)
    {
        var userId = validated.OwnerId ?? throw Invalid();
        var balance = await LockTrackedBalanceAsync(context, userId, cancellationToken);
        var afterClawback = await ComputeSafeBalanceAfterClawbackAsync(
            context, balance, userId, validated.TotalClawback, cancellationToken);
        foreach (var entry in validated.Matches)
            AddPosting(new(context, operation, entry.Header, entry.Plan,
                entry.Original, userId, now, audit));
        balance.CurrentPoints = checked(afterClawback + checked((int)validated.TotalRestoration));
        balance.LastUpdated = now;
        balance.UpdatedAt = now;
        balance.UpdatedBy = audit;
        await ConsumeReservationsAsync(context, operation, validated.TotalClawback, now, audit, cancellationToken);
    }

    private static void AddPosting(PostingInputs input)
    {
        var clawback = input.Plan.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback;
        var delta = clawback ? checked(-input.Plan.RequiredPoints) : input.Plan.RequiredPoints;
        var movement = new FidelityPointsTransaction
        {
            Id = Guid.NewGuid(),
            UserId = input.UserId,
            OrderId = input.Operation.SourceOrderId,
            TransactionType = clawback ? TransactionType.EarnedClawback : TransactionType.RedemptionRestored,
            Points = delta,
            OriginalTransactionId = input.Plan.OriginalTransactionId,
            OrderTotal = input.Original.OrderTotal,
            Description = clawback
                ? "Earned points reversed for accepted order amendment"
                : "Redeemed points restored for accepted order amendment",
            CreatedAt = input.Now,
            CreatedBy = input.Audit
        };
        input.Context.FidelityPointsTransactions.Add(movement);
        input.Context.OrderAmendmentLoyaltyCompensationPostings.Add(new OrderAmendmentLoyaltyCompensationPosting
        {
            Id = Guid.NewGuid(),
            CompensationId = input.Header.Id,
            MovementTransactionId = movement.Id,
            PointsDelta = delta,
            PostedAt = input.Now,
            CreatedAt = input.Now,
            CreatedBy = input.Audit
        });
    }

    internal static bool StoredUnitsMatch(
        IReadOnlyCollection<OrderAmendmentLoyaltyCompensationUnit> storedUnits,
        Guid sourceOrderId, OrderAmendmentLoyaltyCompensationKind kind,
        IReadOnlyCollection<OrderAmendmentLoyaltyCompensationUnitPlan> expectedUnits,
        IReadOnlySet<Guid> acceptedUnitIds)
    {
        if (expectedUnits.Select(value => value.SnapshotUnitId).Distinct().Count() != expectedUnits.Count)
            return false;
        var expectedByUnit = expectedUnits.ToDictionary(value => value.SnapshotUnitId);
        return storedUnits.Count == expectedByUnit.Count
            && storedUnits.Select(value => value.SnapshotUnitId).Distinct().Count() == storedUnits.Count
            && storedUnits.All(value => value.SourceOrderId == sourceOrderId && value.Kind == kind
                && acceptedUnitIds.Contains(value.SnapshotUnitId)
                && expectedByUnit.TryGetValue(value.SnapshotUnitId, out var expected)
                && value.Points == expected.Points);
    }

    private static ConflictException Invalid() => new(
        "The persisted loyalty compensation differs from its reviewed exact unit plan.");
}
