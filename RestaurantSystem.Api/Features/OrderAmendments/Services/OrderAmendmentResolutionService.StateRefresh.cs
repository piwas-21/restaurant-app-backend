using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService
{
    private async Task RefreshOperationStateAsync(
        OrderAmendmentResolutionOperation operation, CancellationToken cancellationToken,
        bool recoverFromReconciliation = false)
    {
        if (operation.State == OrderAmendmentResolutionOperationState.Resolved)
            return;
        var states = await context.OrderAmendmentRefundLegs.AsNoTracking()
            .Where(value => value.OperationId == operation.Id)
            .Select(value => new { value.Id, value.State })
            .ToDictionaryAsync(value => value.Id, value => value.State, cancellationToken);
        foreach (var entry in context.ChangeTracker.Entries<OrderAmendmentRefundLeg>()
                     .Where(entry => entry.Entity.OperationId == operation.Id))
        {
            if (entry.State == EntityState.Deleted)
                states.Remove(entry.Entity.Id);
            else
                states[entry.Entity.Id] = entry.Entity.State;
        }

        if (states.Values.Any(value => value is OrderAmendmentRefundLegState.Failed
                or OrderAmendmentRefundLegState.ReconciliationRequired))
        {
            operation.State = OrderAmendmentResolutionOperationState.ReconciliationRequired;
        }
        else if (operation.State != OrderAmendmentResolutionOperationState.ReconciliationRequired
                 || recoverFromReconciliation)
        {
            operation.State = OrderAmendmentResolutionOperationState.Processing;
        }
        operation.FailureCode = operation.State == OrderAmendmentResolutionOperationState.Processing
            ? null : "provider_refund_requires_reconciliation";
    }
}
