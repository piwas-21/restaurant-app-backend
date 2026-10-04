using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionFinalizer
{
    private async Task LoadAllocationEvidenceAsync(OrderAmendmentRefundLeg[] legs,
        CancellationToken cancellationToken)
    {
        var legIds = legs.Select(value => value.Id).ToArray();
        var allocationIds = legs.SelectMany(value =>
                OrderAmendmentJson.Deserialize<List<OrderAmendmentRefundScope>>(value.FrozenScopesJson))
            .Select(value => value.AllocationId).Distinct().ToArray();
        await context.OrderAmendmentRefundEvidence.Where(value => legIds.Contains(value.RefundLegId))
            .LoadAsync(cancellationToken);
        if (allocationIds.Length > 0)
            await context.AccountPaymentAllocations.Where(value => allocationIds.Contains(value.Id))
                .LoadAsync(cancellationToken);
        if (allocationIds.Length > 0)
            await context.AccountPaymentAllocationReversals.Where(value => allocationIds.Contains(value.AllocationId))
                .LoadAsync(cancellationToken);
    }

}
