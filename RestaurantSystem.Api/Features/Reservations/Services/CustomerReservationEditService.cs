using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class CustomerReservationEditService(ApplicationDbContext context,
    ICustomerReservationCancellationAccess authority, ICustomerReservationEditReadAccess reads,
    ICustomerReservationEditProofs proofs, ICustomerReservationEditor editor,
    IReservationChangedMailer mailer, TimeProvider clock) : ICustomerReservationEditService
{
    public async Task<CustomerReservationEditPreparedDto> PrepareAsync(string sessionId, CustomerReservationEditDto request,
        CancellationToken cancellationToken)
    {
        RequireClean();
        var owner = authority.Capture(sessionId, request.SchemaVersion, request.OperationId, request.TenantId);
        await using var transaction = await ReservationWriteLock.BeginAsync(context, cancellationToken);
        var row = await authority.LockBookingAsync(request.ReservationId, owner, cancellationToken);
        await authority.LockCustomerAsync(owner, cancellationToken);
        var draft = await editor.ReviewAsync(request, row, owner, cancellationToken);
        if (authority.Capture(sessionId, request.SchemaVersion, request.OperationId, request.TenantId) != owner) throw Unavailable();
        var binding = new CustomerReservationEditBinding(row.Id, row.Revision, request.TimeZone, draft.Original.Currency);
        return new(1, proofs.Create(request, owner, binding), draft.Original.Context.Reservation,
            ToItem(row), request.TimeZone);
    }
    public Task<CustomerReservationResolutionDto> SubmitAsync(string sessionId, CustomerReservationEditDto request,
        CancellationToken cancellationToken) => RunAsync(sessionId,
            new(request.SchemaVersion, request.OperationId, request.TenantId, request.PayloadHash ?? string.Empty,
                request.OwnerProof ?? string.Empty), request, false, cancellationToken);
    public Task<CustomerReservationResolutionDto> ResolveAsync(string sessionId, CustomerReservationProofDto proof,
        bool cancelAttempt, CancellationToken cancellationToken) => RunAsync(sessionId, proof, null, cancelAttempt, cancellationToken);

    private async Task<CustomerReservationResolutionDto> RunAsync(string sessionId, CustomerReservationProofDto proof,
        CustomerReservationEditDto? request, bool cancelAttempt, CancellationToken cancellationToken)
    {
        RequireClean();
        var owner = Capture(sessionId, proof);
        var binding = proofs.Require(proof, owner);
        if (request is not null) proofs.RequireInput(request, owner, binding);
        AttemptResult result;
        await using (var transaction = await ReservationWriteLock.BeginAsync(context, cancellationToken))
        {
            try
            {
                result = await DecideUnderLockAsync(sessionId, proof, owner, binding, request, cancelAttempt, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch { context.ChangeTracker.Clear(); throw; }
        }
        if (result.Draft is { } draft)
            await mailer.SendAsync(draft.Reservation, draft.TableNumber, draft.Edit, cancellationToken);
        return result.Resolution;
    }
    private async Task<AttemptResult> DecideUnderLockAsync(string sessionId, CustomerReservationProofDto proof,
        CustomerReservationOperationOwner owner, CustomerReservationEditBinding binding, CustomerReservationEditDto? request,
        bool cancelAttempt, CancellationToken cancellationToken)
    {
        // Journal decisions remain canonical even after the booking is changed, erased or removed.
        var previous = await CustomerReservationDecisions.ReadAsync(context, proof.OperationId, owner, cancellationToken);
        var row = request is not null && previous is null
            ? await authority.LockBookingAsync(binding.ReservationId, owner, cancellationToken) : null;
        await authority.LockCustomerAsync(owner, cancellationToken);
        if (Capture(sessionId, proof) != owner || proofs.Require(proof, owner) != binding) throw Unavailable();
        if (request is not null) proofs.RequireInput(request, owner, binding);
        if (previous is not null) return new(CustomerReservationDecisions.ToResolution(previous), null);
        if (request is null && !cancelAttempt)
            return new(new(1, proof.OperationId, CustomerReservationOperationOutcome.Unseen, null), null);
        var draft = row is null || request is null ? null
            : await ApplyUnderLockAsync(request, row, owner, binding, cancellationToken);
        var applied = draft is null ? null : await reads.ReadAsync(draft.Reservation.Id, cancellationToken);
        if (applied is not null && applied.Currency != binding.Currency) throw Unavailable();
        if (applied is not null && applied.Revision != draft?.Reservation.Revision)
            throw new ConflictException("This booking changed before its receipt could be saved.");
        context.Set<CustomerReservationOperation>().Add(new CustomerReservationOperation
        {
            OperationId = proof.OperationId,
            OwnerHash = owner.OwnerHash,
            PayloadHash = owner.PayloadHash,
            ReservationId = draft?.Reservation.Id,
            IsCancelled = cancelAttempt,
            CompletedAt = clock.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync(cancellationToken);
        if (draft is not null && request is not null && applied is not null)
        {
            await editor.RevalidateAsync(request, applied, draft.Edit, cancellationToken);
            if (Capture(sessionId, proof) != owner) throw Unavailable();
        }
        return new(new(1, proof.OperationId, cancelAttempt ? CustomerReservationOperationOutcome.Cancelled
            : CustomerReservationOperationOutcome.Applied, draft?.Reservation.Id), draft);
    }
    private async Task<CustomerReservationEditDraft> ApplyUnderLockAsync(CustomerReservationEditDto request, Reservation row,
        CustomerReservationOperationOwner owner, CustomerReservationEditBinding binding, CancellationToken cancellationToken)
    {
        if (row.Revision != binding.Revision)
            throw new ConflictException("This booking changed. Review its current details before editing.");
        if (request.TimeZone != binding.TimeZone) throw Unavailable();
        var draft = await editor.ReviewAsync(request, row, owner, cancellationToken);
        if (draft.Original.Context.TimeZone != binding.TimeZone || draft.Original.Currency != binding.Currency) throw Unavailable();
        var updated = await context.Reservations.Where(value => value.Id == row.Id
                && value.CustomerId == owner.CustomerId && value.Revision == binding.Revision)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.CustomerName, row.CustomerName)
                .SetProperty(value => value.CustomerEmail, row.CustomerEmail).SetProperty(value => value.CustomerPhone, row.CustomerPhone)
                .SetProperty(value => value.SpecialRequests, row.SpecialRequests).SetProperty(value => value.ReservationDate, row.ReservationDate)
                .SetProperty(value => value.StartTime, row.StartTime).SetProperty(value => value.EndTime, row.EndTime)
                .SetProperty(value => value.NumberOfGuests, row.NumberOfGuests).SetProperty(value => value.Status, row.Status)
                .SetProperty(value => value.UpdatedAt, clock.GetUtcNow().UtcDateTime)
                .SetProperty(value => value.UpdatedBy, authority.AuditIdentifier), cancellationToken);
        if (updated != 1) throw Unavailable();
        // ExecuteUpdate leaves the detached input revision unchanged. Mail must use the saved database snapshot.
        var saved = await CustomerReservationReadAccess.Scope(context, draft.Original.Owner)
            .SingleAsync(value => value.Id == row.Id, cancellationToken);
        return draft with { Reservation = saved };
    }
    private CustomerReservationOperationOwner Capture(string sessionId, CustomerReservationProofDto proof) =>
        authority.Capture(sessionId, proof.SchemaVersion, proof.OperationId, proof.TenantId) with { PayloadHash = proof.PayloadHash };
    private static CustomerReservationItemDto ToItem(Reservation row) => new(row.Id, DateOnly.FromDateTime(row.ReservationDate),
        row.StartTime, row.EndTime, row.NumberOfGuests, row.Status, row.SpecialRequests);
    private void RequireClean()
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Native booking operations require an isolated clean context.");
    }
    private static NotFoundException Unavailable() => new("Customer reservation operation unavailable.");
    private sealed record AttemptResult(CustomerReservationResolutionDto Resolution, CustomerReservationEditDraft? Draft);
}
