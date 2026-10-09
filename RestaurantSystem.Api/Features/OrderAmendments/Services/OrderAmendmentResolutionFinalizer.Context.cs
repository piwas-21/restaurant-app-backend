using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionFinalizer
{
    private void ResetVerifiedCleanRequestContext()
    {
        if (context.Database.CurrentTransaction is not null)
            throw new ConflictException("Refund finalization must start outside a database transaction.");
        if (context.ChangeTracker.HasChanges())
            throw new ConflictException("Pending database changes must be committed before refund finalization.");

        // Provider I/O and its evidence are persisted through separate scopes. Discard only this
        // verified-clean request snapshot so the locked finalization reads current account revisions.
        context.ChangeTracker.Clear();
    }
}
