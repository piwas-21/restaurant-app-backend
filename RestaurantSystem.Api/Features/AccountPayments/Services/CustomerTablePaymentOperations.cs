using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTablePaymentOperations(ApplicationDbContext context, ICustomerTablePaymentAccess access,
    ICustomerTablePaymentProofs proofs, ICustomerTablePaymentAcceptance acceptance, TimeProvider clock)
    : ICustomerTablePaymentOperations
{
    public Task<CustomerTablePaymentResolutionDto> SubmitAsync(string sessionId, string? participantToken,
        CustomerTablePaymentSubmissionDto request, CancellationToken cancellationToken)
    {
        if (request.Intent is null || request.Proof is null)
            throw new BadRequestException("The original contribution intent and proof are required.");
        return RunAsync(sessionId, request.Proof, request.Intent, participantToken, false, cancellationToken);
    }

    public Task<CustomerTablePaymentResolutionDto> ResolveAsync(string sessionId, CustomerTablePaymentProofDto proof,
        bool stop, CancellationToken cancellationToken) => RunAsync(sessionId, proof, null, null, stop, cancellationToken);

    private async Task<CustomerTablePaymentResolutionDto> RunAsync(string sessionId, CustomerTablePaymentProofDto proof,
        CustomerTablePaymentIntentDto? intent, string? token, bool stop, CancellationToken cancellationToken)
    {
        if (proof is null) throw new BadRequestException("The original contribution proof is required.");
        var owner = access.Capture(sessionId, proof.SchemaVersion, proof.TenantId);
        var binding = proofs.Require(proof, owner);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new ConflictException("Native contribution decisions require an isolated clean context.");
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            // Match shared writers: visit precedes customer authority and payment operation locks.
            await TableServiceSessionRowLock.LoadAsync(context, binding.ServiceSessionId, cancellationToken);
            if (owner.CustomerId is { } customer)
                await CustomerAccountWriteGuard.AcquireAsync(context, customer, true, cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({CustomerTablePaymentHashes.Purpose + proof.OperationId.ToString("N")}, 0))",
                cancellationToken);
            RequireOwner(sessionId, proof, owner);
            var previous = await context.Set<CustomerTablePaymentOperation>().SingleOrDefaultAsync(
                row => row.OperationId == proof.OperationId, cancellationToken);
            if (previous is not null && (previous.OwnerHash != owner.OwnerHash || previous.PayloadHash != proof.PayloadHash))
                throw new NotFoundException("Customer table payment operation unavailable.");
            if (intent is not null) proofs.RequireInput(intent, owner, binding, proof);
            CustomerTablePaymentResolutionDto result;
            if (previous is not null) result = await ResolutionAsync(previous, binding, cancellationToken);
            else if (intent is null && !stop)
                result = new(1, proof.OperationId, binding.ServiceSessionId, CustomerTablePaymentOutcome.Unseen, null, null);
            else
            {
                var attemptId = intent is null ? (Guid?)null
                    : await acceptance.AcceptAsync(owner, proof, binding, intent, token, cancellationToken);
                var receipt = new CustomerTablePaymentOperation
                {
                    OperationId = proof.OperationId,
                    OwnerHash = owner.OwnerHash,
                    PayloadHash = proof.PayloadHash,
                    AttemptId = attemptId,
                    IsStopped = stop,
                    CompletedAt = clock.GetUtcNow().UtcDateTime
                };
                context.Set<CustomerTablePaymentOperation>().Add(receipt);
                await context.SaveChangesAsync(cancellationToken);
                result = await ResolutionAsync(receipt, binding, cancellationToken);
            }
            RequireOwner(sessionId, proof, owner);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    private async Task<CustomerTablePaymentResolutionDto> ResolutionAsync(CustomerTablePaymentOperation receipt,
        CustomerTablePaymentBinding binding, CancellationToken cancellationToken)
    {
        if (receipt.IsStopped && receipt.AttemptId is null)
            return new(1, receipt.OperationId, binding.ServiceSessionId, CustomerTablePaymentOutcome.Stopped, null, null);
        var attempt = await context.AccountPaymentAttempts.AsNoTracking().Where(row => row.Id == receipt.AttemptId
            && row.OperationId == binding.GuestOperationId && row.ServiceSessionId == binding.ServiceSessionId
            && row.ActorId == binding.ParticipantId && row.ActorKind == AccountPaymentActorKind.GuestParticipant
            && row.AmountMinor == binding.AmountMinor && row.Currency == binding.Currency
            && row.ExpectedAccountRevision == binding.AccountRevision && row.PaymentMethod == PaymentMethod.OnlinePayment
            && row.TipMinor == 0)
            .Select(row => new
            {
                row.Id,
                row.State,
                row.SnapshotJson,
                Allocations = row.Allocations.Take(CustomerTablePaymentHashes.MaximumSegments + 1).Select(value =>
                    new AccountPaymentAllocationDto(value.OrderId, value.OrderItemId, value.StartOrdinal,
                        value.UnitCount, value.MinorPerUnit, value.AmountMinor)).ToArray()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ConflictException("The original contribution requires reconciliation. Do not replace it.");
        // Retain the prepared ordering from the immutable quote; SQL child order is unspecified.
        var frozen = AccountPaymentSnapshots.Deserialize<AccountPaymentQuoteSnapshot>(attempt.SnapshotJson);
        var sorted = frozen.Allocations.OrderBy(row => row.OrderId).ThenBy(row => row.OrderItemId)
            .ThenBy(row => row.StartOrdinal).ToArray();
        if (frozen.ExpectedAccountRevision != binding.AccountRevision || frozen.AmountMinor != binding.AmountMinor
            || frozen.Currency != binding.Currency || CustomerBasketOperationHash.Payload(frozen.Allocations) != binding.ScopeHash
            || !attempt.Allocations.OrderBy(row => row.OrderId).ThenBy(row => row.OrderItemId)
                .ThenBy(row => row.StartOrdinal).SequenceEqual(sorted))
            throw new ConflictException("The original contribution allocation requires reconciliation. Do not replace it.");
        return new(1, receipt.OperationId, binding.ServiceSessionId, CustomerTablePaymentOutcome.Applied, attempt.Id, attempt.State);
    }

    private void RequireOwner(string sessionId, CustomerTablePaymentProofDto proof, CustomerTablePaymentOwner owner)
    {
        if (access.Capture(sessionId, proof.SchemaVersion, proof.TenantId) != owner)
            throw new NotFoundException("Customer table payment operation unavailable.");
    }
}
