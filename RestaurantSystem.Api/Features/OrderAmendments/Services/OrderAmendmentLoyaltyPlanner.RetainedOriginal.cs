using System.Diagnostics.CodeAnalysis;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static partial class OrderAmendmentLoyaltyPlanner
{
    private static bool MatchesRetainedOwner(OrderBillingSnapshotOwnerLink owner,
        FidelityPointsTransaction transaction)
    {
        if (owner.Disposition == OrderBillingSnapshotOwnerDisposition.Linked)
            return owner.UserId.HasValue && !owner.ErasedAt.HasValue && owner.ErasureTransactionId is null
                && transaction.UserId == owner.UserId;
        return MatchesErasedOwner(owner) && transaction.UserId is null;
    }

    private static bool MatchesErasedOwner(OrderBillingSnapshotOwnerLink owner) =>
        owner.Disposition == OrderBillingSnapshotOwnerDisposition.Erased
        && !owner.UserId.HasValue && owner.ErasedAt.HasValue
        && owner.ErasureTransactionId is { Length: >= 1 and <= 20 } erasureId
        && erasureId.All(char.IsAsciiDigit);

    internal static bool TryReadRetainedOriginal(OrderAmendmentLoyaltyCompensation header,
        OrderBillingSnapshotOwnerLink owner, OrderBillingSnapshot? snapshot,
        IReadOnlyDictionary<Guid, FidelityPointsTransaction> originalRows, Guid sourceOrderId,
        [NotNullWhen(true)] out FidelityPointsTransaction? original)
    {
        if (originalRows.TryGetValue(header.OriginalTransactionId, out var persisted) && persisted is not null)
        {
            original = persisted;
            return MatchesRetainedOwner(owner, persisted);
        }

        if (header.Kind != OrderAmendmentLoyaltyCompensationKind.RedemptionRestoration
            || owner.Slot != OrderBillingSnapshotOwnerSlot.Redemption
            || owner.Disposition != OrderBillingSnapshotOwnerDisposition.Erased
            || snapshot is null || snapshot.OrderId != sourceOrderId
            || snapshot.RedemptionTransactionId != header.OriginalTransactionId
            || snapshot.RedemptionTransactionType != TransactionType.Redeemed
            || snapshot.RedemptionTransactionPoints != header.OriginalTransactionPoints
            || snapshot.RedemptionTransactionPoints.GetValueOrDefault() >= 0
            || -snapshot.RedemptionTransactionPoints.GetValueOrDefault() != snapshot.RedeemedPoints
            || !snapshot.RedemptionTransactionCreatedAt.HasValue
            || snapshot.RedemptionTransactionCreatedAt.Value == default
            || !MatchesErasedOwner(owner))
        {
            original = null;
            return false;
        }

        original = new FidelityPointsTransaction
        {
            Id = header.OriginalTransactionId,
            UserId = null,
            OrderId = sourceOrderId,
            TransactionType = TransactionType.Redeemed,
            Points = snapshot.RedemptionTransactionPoints.Value,
            OrderTotal = snapshot.RedemptionTransactionOrderTotal,
            CreatedAt = snapshot.RedemptionTransactionCreatedAt.Value,
            CreatedBy = "RetainedLoyaltyEvidence"
        };
        return true;
    }
}
