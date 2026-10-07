using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.OrderAmendments;

public sealed class OrderAmendmentLoyaltyRetainedSourceTests
{
    [Fact]
    public void Stored_compensation_units_match_by_identity_when_persisted_order_is_shuffled()
    {
        var orderId = Guid.NewGuid();
        var first = Guid.Parse("f0000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("0f000000-0000-0000-0000-000000000002");
        var expected = new[]
        {
            new OrderAmendmentLoyaltyCompensationUnitPlan(first, 10),
            new OrderAmendmentLoyaltyCompensationUnitPlan(second, 20)
        };
        var databaseOrdered = new[]
        {
            Unit(second, orderId, 20),
            Unit(first, orderId, 10)
        };

        Assert.NotEqual(expected.Select(unit => unit.SnapshotUnitId).ToArray(),
            databaseOrdered.Select(unit => unit.SnapshotUnitId).ToArray());
        Assert.True(OrderAmendmentLoyaltyCompensationPoster.StoredUnitsMatch(databaseOrdered,
            orderId, OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration,
            expected, new HashSet<Guid> { first, second }));
    }

    [Fact]
    public void Erased_redemption_restoration_uses_exact_frozen_debit_when_source_row_was_deleted()
    {
        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var owner = new OrderBillingSnapshotOwnerLink
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Slot = OrderBillingSnapshotOwnerSlot.Redemption,
            UserId = null,
            Disposition = OrderBillingSnapshotOwnerDisposition.Erased,
            ErasedAt = DateTime.UtcNow,
            ErasureTransactionId = "987654321",
            CreatedBy = "RetainedSourceTest"
        };
        var snapshot = new OrderBillingSnapshot
        {
            OrderId = orderId,
            RedemptionTransactionId = transactionId,
            RedemptionTransactionType = TransactionType.Redeemed,
            RedemptionTransactionPoints = -35,
            RedemptionTransactionOrderTotal = null,
            RedemptionTransactionCreatedAt = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc),
            RedeemedPoints = 35,
            CreatedBy = "RetainedSourceTest"
        };
        var compensation = new OrderAmendmentLoyaltyCompensation
        {
            OriginalTransactionId = transactionId,
            OriginalTransactionPoints = -35,
            Kind = OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration,
            CreatedBy = "RetainedSourceTest"
        };

        var found = OrderAmendmentLoyaltyPlanner.TryReadRetainedOriginal(compensation, owner,
            snapshot, new Dictionary<Guid, FidelityPointsTransaction>(), orderId, out var original);

        Assert.True(found);
        Assert.Equal(transactionId, original.Id);
        Assert.Equal(orderId, original.OrderId);
        Assert.Null(original.UserId);
        Assert.Equal(TransactionType.Redeemed, original.TransactionType);
        Assert.Equal(-35, original.Points);
        Assert.Null(original.OrderTotal);
        Assert.Equal(snapshot.RedemptionTransactionCreatedAt, original.CreatedAt);
    }

    [Theory]
    [InlineData("012345678901234567890")]
    [InlineData("not-a-database-transaction-id")]
    public void Erased_redemption_source_requires_valid_published_erasure_transaction_id(string erasureId)
    {
        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var owner = new OrderBillingSnapshotOwnerLink
        {
            OrderId = orderId,
            Slot = OrderBillingSnapshotOwnerSlot.Redemption,
            Disposition = OrderBillingSnapshotOwnerDisposition.Erased,
            ErasedAt = DateTime.UtcNow,
            ErasureTransactionId = erasureId,
            CreatedBy = "RetainedSourceTest"
        };
        var snapshot = new OrderBillingSnapshot
        {
            OrderId = orderId,
            RedemptionTransactionId = transactionId,
            RedemptionTransactionType = TransactionType.Redeemed,
            RedemptionTransactionPoints = -1,
            RedemptionTransactionCreatedAt = DateTime.UtcNow,
            RedeemedPoints = 1,
            CreatedBy = "RetainedSourceTest"
        };
        var compensation = new OrderAmendmentLoyaltyCompensation
        {
            OriginalTransactionId = transactionId,
            OriginalTransactionPoints = -1,
            Kind = OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration,
            CreatedBy = "RetainedSourceTest"
        };

        Assert.False(OrderAmendmentLoyaltyPlanner.TryReadRetainedOriginal(compensation, owner,
            snapshot, new Dictionary<Guid, FidelityPointsTransaction>(), orderId, out _));
    }

    [Fact]
    public void Erased_redemption_source_rejects_snapshot_that_does_not_match_compensation()
    {
        var orderId = Guid.NewGuid();
        var owner = new OrderBillingSnapshotOwnerLink
        {
            OrderId = orderId,
            Slot = OrderBillingSnapshotOwnerSlot.Redemption,
            Disposition = OrderBillingSnapshotOwnerDisposition.Erased,
            ErasedAt = DateTime.UtcNow,
            ErasureTransactionId = "987654321",
            CreatedBy = "RetainedSourceTest"
        };
        var snapshot = new OrderBillingSnapshot
        {
            OrderId = orderId,
            RedemptionTransactionId = Guid.NewGuid(),
            RedemptionTransactionType = TransactionType.Redeemed,
            RedemptionTransactionPoints = -35,
            RedemptionTransactionCreatedAt = DateTime.UtcNow,
            RedeemedPoints = 35,
            CreatedBy = "RetainedSourceTest"
        };
        var compensation = new OrderAmendmentLoyaltyCompensation
        {
            OriginalTransactionId = Guid.NewGuid(),
            OriginalTransactionPoints = -34,
            Kind = OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration,
            CreatedBy = "RetainedSourceTest"
        };

        Assert.False(OrderAmendmentLoyaltyPlanner.TryReadRetainedOriginal(compensation, owner,
            snapshot, new Dictionary<Guid, FidelityPointsTransaction>(), orderId, out _));
    }

    private static OrderAmendmentLoyaltyCompensationUnit Unit(Guid id, Guid orderId, int points) => new()
    {
        SnapshotUnitId = id,
        SourceOrderId = orderId,
        Kind = OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration,
        Points = points,
        CreatedBy = "RetainedSourceTest"
    };
}
