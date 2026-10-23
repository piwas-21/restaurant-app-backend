using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTablePaymentOperations(ApplicationDbContext context, ICustomerTablePaymentAccess access,
    ICustomerTablePaymentProofs proofs, ICustomerTablePaymentAcceptance acceptance, ICustomerTablePaymentDecisions decisions, TimeProvider clock)
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
            var previous = await decisions.ReadAsync(owner, proof, binding, cancellationToken);
            if (intent is not null) proofs.RequireInput(intent, owner, binding, proof);
            CustomerTablePaymentResolutionDto result;
            if (previous is not null) result = previous;
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
                result = await decisions.ReadAsync(owner, proof, binding, cancellationToken)
                    ?? throw new ConflictException("The original contribution decision was not retained.");
            }
            RequireOwner(sessionId, proof, owner);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    private void RequireOwner(string sessionId, CustomerTablePaymentProofDto proof, CustomerTablePaymentOwner owner)
    {
        if (access.Capture(sessionId, proof.SchemaVersion, proof.TenantId) != owner)
            throw new NotFoundException("Customer table payment operation unavailable.");
    }
}
