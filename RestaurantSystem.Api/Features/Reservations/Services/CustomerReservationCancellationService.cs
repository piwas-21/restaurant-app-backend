using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class CustomerReservationCancellationService(ApplicationDbContext context,
    ICustomerReservationCancellationAccess access, ICustomerReservationCancellationProofs proofs,
    IReservationRejectedMailer mailer, TimeProvider clock) : ICustomerReservationCancellationService
{
    public async Task<CustomerReservationCancellationPreparedDto> PrepareAsync(string sessionId,
        CustomerReservationCancellationDto request, CancellationToken cancellationToken)
    {
        RequireClean();
        var owner = access.Capture(sessionId, request.SchemaVersion, request.OperationId, request.TenantId);
        await using var transaction = await ReservationWriteLock.BeginAsync(context, cancellationToken);
        var row = await access.LockBookingAsync(request.ReservationId, owner, cancellationToken);
        await access.LockCustomerAsync(owner, cancellationToken);
        await access.RequireAcceptingAsync(request.TenantId, request.TimeZone, cancellationToken);
        if (access.Capture(sessionId, request.SchemaVersion, request.OperationId, request.TenantId) != owner) throw Unavailable();
        access.EnsureEditable(row);
        var proof = proofs.Create(request.TenantId, request.OperationId, owner, new(row.Id, row.Revision, request.TimeZone));
        // Preparation holds no capacity, creates no decision and returns only the customer projection.
        return new(1, proof, new(row.Id, DateOnly.FromDateTime(row.ReservationDate), row.StartTime,
            row.EndTime, row.NumberOfGuests, row.Status, row.SpecialRequests), request.TimeZone);
    }
    public Task<CustomerReservationResolutionDto> SubmitAsync(string sessionId, CustomerReservationProofDto proof,
        CancellationToken cancellationToken) => RunAsync(sessionId, proof, true, false, cancellationToken);
    public Task<CustomerReservationResolutionDto> ResolveAsync(string sessionId, CustomerReservationProofDto proof,
        bool cancelAttempt, CancellationToken cancellationToken) => RunAsync(sessionId, proof, false, cancelAttempt, cancellationToken);

    private async Task<CustomerReservationResolutionDto> RunAsync(string sessionId, CustomerReservationProofDto proof,
        bool submit, bool cancelAttempt, CancellationToken cancellationToken)
    {
        RequireClean();
        var owner = Capture(sessionId, proof);
        var binding = proofs.Require(proof, owner);
        AttemptResult result;
        await using (var transaction = await ReservationWriteLock.BeginAsync(context, cancellationToken))
        {
            try
            {
                result = await DecideUnderLockAsync(sessionId, proof, owner, binding, submit, cancelAttempt, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch { context.ChangeTracker.Clear(); throw; }
        }
        if (result.Cancelled is { } cancelled)
        {
            cancelled.Status = ReservationStatus.Cancelled;
            await mailer.SendAsync(cancelled, cancellationToken);
        }
        return result.Resolution;
    }
    private async Task<AttemptResult> DecideUnderLockAsync(string sessionId, CustomerReservationProofDto proof,
        CustomerReservationOperationOwner owner, CustomerReservationCancellationBinding binding, bool submit,
        bool cancelAttempt, CancellationToken cancellationToken)
    {
        // Canonical decisions do not depend on the booking's current state, revision or existence.
        var previous = await CustomerReservationDecisions.ReadAsync(context, proof.OperationId, owner, cancellationToken);
        var row = submit && previous is null
            ? await access.LockBookingAsync(binding.ReservationId, owner, cancellationToken) : null;
        await access.LockCustomerAsync(owner, cancellationToken);
        if (Capture(sessionId, proof) != owner || proofs.Require(proof, owner) != binding) throw Unavailable();
        if (previous is not null) return new(CustomerReservationDecisions.ToResolution(previous), null);
        if (!submit && !cancelAttempt)
            return new(new(1, proof.OperationId, CustomerReservationOperationOutcome.Unseen, null), null);
        if (row is not null) await ApplyUnderLockAsync(proof, binding, owner, row, cancellationToken);
        var receipt = new CustomerReservationOperation
        {
            OperationId = proof.OperationId,
            OwnerHash = owner.OwnerHash,
            PayloadHash = owner.PayloadHash,
            ReservationId = row?.Id,
            IsCancelled = cancelAttempt,
            CompletedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Set<CustomerReservationOperation>().Add(receipt);
        await context.SaveChangesAsync(cancellationToken);
        if (row is not null)
        {
            await access.RequireAcceptingAsync(proof.TenantId, binding.TimeZone, cancellationToken);
            access.EnsureEditable(row);
            if (Capture(sessionId, proof) != owner) throw Unavailable();
        }
        return new(CustomerReservationDecisions.ToResolution(receipt), row);
    }
    private async Task ApplyUnderLockAsync(CustomerReservationProofDto proof, CustomerReservationCancellationBinding binding,
        CustomerReservationOperationOwner owner, Reservation row, CancellationToken cancellationToken)
    {
        await access.RequireAcceptingAsync(proof.TenantId, binding.TimeZone, cancellationToken);
        access.EnsureEditable(row);
        if (row.Revision != binding.Revision)
            throw new ConflictException("This booking changed. Review its current details before cancelling.");
        var updated = await context.Reservations.Where(value => value.Id == row.Id
                && value.CustomerId == owner.CustomerId && value.Revision == binding.Revision)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.Status, ReservationStatus.Cancelled)
                .SetProperty(value => value.UpdatedAt, clock.GetUtcNow().UtcDateTime)
                .SetProperty(value => value.UpdatedBy, access.AuditIdentifier), cancellationToken);
        if (updated != 1) throw Unavailable();
    }
    private sealed record AttemptResult(CustomerReservationResolutionDto Resolution, Reservation? Cancelled);
    private CustomerReservationOperationOwner Capture(string sessionId, CustomerReservationProofDto proof) =>
        access.Capture(sessionId, proof.SchemaVersion, proof.OperationId, proof.TenantId) with { PayloadHash = proof.PayloadHash };
    private void RequireClean()
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Native booking operations require an isolated clean context.");
    }
    private static NotFoundException Unavailable() => new("Customer reservation operation unavailable.");
}
