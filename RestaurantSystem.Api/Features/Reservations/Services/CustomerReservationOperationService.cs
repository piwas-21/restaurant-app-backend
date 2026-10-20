using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class CustomerReservationOperationService(ApplicationDbContext context,
    ICustomerReservationOperationGuard guard, ICustomerReservationCreator creator,
    IReservationCreatedMailer mailer, TimeProvider clock) : ICustomerReservationOperationService
{
    public async Task<CustomerReservationResolutionDto> RunAsync(string sessionId, CustomerReservationCreateDto request,
        bool cancelAttempt, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Native booking operations require an isolated clean context.");
        var owner = guard.Capture(sessionId, request);
        ReservationCreationDraft? draft = null;
        CustomerReservationOperation receipt;
        // Schedule always precedes customer-row authority; all booking writers share this transaction lock.
        await using (var transaction = await ReservationWriteLock.BeginAsync(context, cancellationToken))
        {
            try
            {
                await guard.LockOwnerAsync(owner, cancellationToken);
                // Waiting must not let a reassigned tenant or changed principal recover/seal the old scope.
                if (guard.Capture(sessionId, request) != owner)
                    throw new NotFoundException("Customer reservation operation unavailable.");
                var previous = await ReadPreviousAsync(request.OperationId, owner, cancellationToken);
                if (previous is not null)
                {
                    // A cancel-attempt decision cannot cancel an already applied booking, or replay mail.
                    await transaction.CommitAsync(cancellationToken);
                    return Resolution(previous);
                }
                if (!cancelAttempt) draft = await creator.StageAsync(request, owner, cancellationToken);
                if (!cancelAttempt && (draft?.Reservation is null || draft.Reservation.Id == Guid.Empty || draft.TableNumber is null))
                    throw new InvalidOperationException("Booking acceptance must stage its reservation and mail destination.");
                receipt = new CustomerReservationOperation
                {
                    OperationId = request.OperationId,
                    OwnerHash = owner.OwnerHash,
                    PayloadHash = owner.PayloadHash,
                    ReservationId = draft?.Reservation?.Id,
                    IsCancelled = cancelAttempt,
                    CompletedAt = clock.GetUtcNow().UtcDateTime
                };
                context.Set<CustomerReservationOperation>().Add(receipt);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                // Rollback alone leaves tracked inserts which a later SaveChanges could resurrect.
                context.ChangeTracker.Clear();
                throw;
            }
        }
        if (draft?.Reservation is { } reservation && draft.TableNumber is { } tableNumber)
            await mailer.SendAsync(reservation, tableNumber, cancellationToken);
        return Resolution(receipt);
    }

    private async Task<CustomerReservationOperation?> ReadPreviousAsync(Guid operationId,
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

    private static CustomerReservationResolutionDto Resolution(CustomerReservationOperation row) => new(1,
        row.OperationId, row.IsCancelled ? CustomerReservationOperationOutcome.Cancelled : CustomerReservationOperationOutcome.Applied,
        row.ReservationId);
}
