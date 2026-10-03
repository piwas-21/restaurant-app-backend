using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Stops amendments from changing item scopes with money already claimed by the account ledger.</summary>
public sealed class AccountPaymentOrderAmendmentReservationGuard(
    ApplicationDbContext context) : IOrderAmendmentReservationGuard
{
    public async Task AssertUnitsMutableAsync(
        Guid orderId,
        IReadOnlyList<OrderAmendmentUnitScope> scopes,
        CancellationToken cancellationToken)
    {
        if (scopes.Count == 0)
            return;

        var knownUnprotected = AccountPaymentStateRules.KnownUnprotectedStates;
        var allocations = await context.AccountPaymentAllocations.AsNoTracking()
            .Where(value => value.OrderId == orderId && !knownUnprotected.Contains(value.Attempt!.State))
            .Select(value => new { value.OrderItemId, value.StartOrdinal, value.UnitCount, State = value.Attempt!.State })
            .ToListAsync(cancellationToken);

        var intersects = allocations.Any(allocation => allocation.OrderItemId is null
            || scopes.Any(scope => scope.OrderItemId == allocation.OrderItemId
                && !(allocation.State == AccountPaymentState.Captured && scope.AllowCapturedReversal)
                && (scope.WholeLine
                    || allocation.StartOrdinal < (long)scope.StartOrdinal + scope.Count
                    && (long)allocation.StartOrdinal + allocation.UnitCount > scope.StartOrdinal)));
        if (intersects)
        {
            throw new ConflictException(
                "Resolve the reserved or allocated payment for the selected item quantity before amending it.");
        }
    }
}
