using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

internal static class CustomerReservationDecisions
{
    internal static async Task<CustomerReservationOperation?> ReadAsync(ApplicationDbContext context, Guid operationId,
        CustomerReservationOperationOwner owner, CancellationToken cancellationToken)
    {
        var previous = await context.Set<CustomerReservationOperation>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.OperationId == operationId, cancellationToken);
        if (previous is null) return null;
        if (!string.Equals(previous.OwnerHash, owner.OwnerHash, StringComparison.Ordinal))
            throw new NotFoundException("Customer reservation operation unavailable.");
        if (!string.Equals(previous.PayloadHash, owner.PayloadHash, StringComparison.Ordinal))
            throw new ConflictException("This operation ID was already used for different reservation details.");
        return previous;
    }

    internal static CustomerReservationResolutionDto ToResolution(CustomerReservationOperation row) => new(1,
        row.OperationId, row.IsCancelled ? CustomerReservationOperationOutcome.Cancelled : CustomerReservationOperationOutcome.Applied,
        row.ReservationId);
}
