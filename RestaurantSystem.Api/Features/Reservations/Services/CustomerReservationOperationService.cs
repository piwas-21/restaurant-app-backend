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
    public Task<CustomerReservationResolutionDto> CreateAsync(string sessionId, CustomerReservationCreateDto request,
        CancellationToken cancellationToken) => RunAsync(sessionId,
            new(request.SchemaVersion, request.OperationId, request.TenantId, request.PayloadHash ?? string.Empty, request.OwnerProof ?? string.Empty),
            async owner =>
            {
                if (guard.Capture(sessionId, request) != owner)
                    throw new NotFoundException("Customer reservation operation unavailable.");
                var draft = await creator.StageAsync(request, owner, cancellationToken);
                guard.EnsureFutureSlot(request);
                return draft;
            }, () => guard.Capture(sessionId, request), false, cancellationToken);

    public Task<CustomerReservationResolutionDto> ResolveAsync(string sessionId, CustomerReservationProofDto proof,
        bool cancelAttempt, CancellationToken cancellationToken) =>
        RunAsync(sessionId, proof, null, null, cancelAttempt, cancellationToken);

    private async Task<CustomerReservationResolutionDto> RunAsync(string sessionId, CustomerReservationProofDto proof,
        Func<CustomerReservationOperationOwner, Task<ReservationCreationDraft>>? create,
        Func<CustomerReservationOperationOwner>? verifyInput, bool cancelAttempt, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Native booking operations require an isolated clean context.");
        var owner = guard.CaptureProof(sessionId, proof);
        AttemptResult result;
        // Schedule always precedes customer-row authority; all booking writers share this transaction lock.
        await using (var transaction = await ReservationWriteLock.BeginAsync(context, cancellationToken))
        {
            try
            {
                await guard.LockOwnerAsync(owner, cancellationToken);
                // Waiting must not let a reassigned tenant or changed principal recover/seal the old scope.
                if (guard.CaptureProof(sessionId, proof) != owner)
                    throw new NotFoundException("Customer reservation operation unavailable.");
                result = await DecideUnderLockAsync(proof, owner, create, verifyInput, cancelAttempt, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                // Rollback alone leaves tracked inserts which a later SaveChanges could resurrect.
                context.ChangeTracker.Clear();
                throw;
            }
        }
        if (result.Draft?.Reservation is { } reservation && result.Draft.TableNumber is { } tableNumber)
            await mailer.SendAsync(reservation, tableNumber, cancellationToken);
        return result.Resolution;
    }

    private async Task<AttemptResult> DecideUnderLockAsync(CustomerReservationProofDto proof,
        CustomerReservationOperationOwner owner,
        Func<CustomerReservationOperationOwner, Task<ReservationCreationDraft>>? create,
        Func<CustomerReservationOperationOwner>? verifyInput, bool cancelAttempt, CancellationToken cancellationToken)
    {
        var previous = await ReadPreviousAsync(proof.OperationId, owner, cancellationToken);
        // Check original owner before full-body verification, retaining uniform foreign-owner404.
        verifyInput?.Invoke();
        // Cancel-attempt cannot cancel an applied booking, and recovery never replays mail.
        if (previous is not null) return new(Resolution(previous), null);
        if (create is null && !cancelAttempt)
            return new(new(1, proof.OperationId, CustomerReservationOperationOutcome.Unseen, null), null);
        var draft = create is null ? null : await create(owner);
        if (create is not null && (draft?.Reservation is null || draft.Reservation.Id == Guid.Empty || draft.TableNumber is null))
            throw new InvalidOperationException("Booking acceptance must stage its reservation and mail destination.");
        var receipt = new CustomerReservationOperation
        {
            OperationId = proof.OperationId,
            OwnerHash = owner.OwnerHash,
            PayloadHash = owner.PayloadHash,
            ReservationId = draft?.Reservation?.Id,
            IsCancelled = cancelAttempt,
            CompletedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Set<CustomerReservationOperation>().Add(receipt);
        await context.SaveChangesAsync(cancellationToken);
        return new(Resolution(receipt), draft);
    }

    private sealed record AttemptResult(CustomerReservationResolutionDto Resolution, ReservationCreationDraft? Draft);

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
