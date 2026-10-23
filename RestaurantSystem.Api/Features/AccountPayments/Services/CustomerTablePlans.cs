using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTablePlans(ApplicationDbContext context, ICustomerTablePaymentAccess access,
    ITableGuestParticipantPaymentAuthorization participants, IAccountEqualSharePlanService plans,
    ICustomerTablePlanProofs proofs, ICustomerTablePlanDecisions decisions, TimeProvider clock,
    IOptions<AccountPaymentSettings> limits) : ICustomerTablePlans
{
    public async Task<CustomerTablePlanPreparedDto> PrepareAsync(string sessionId, string? participantToken,
        CustomerTablePlanIntentDto request, CancellationToken cancellationToken)
    {
        var shared = SharedRequest(request, request.OperationId);
        if (!TableGuestCredentialCrypto.TryHashParticipantToken(participantToken, out var tokenHash)) throw Unavailable();
        var owner = access.Capture(sessionId, request.SchemaVersion, request.TenantId);
        var currency = await access.RequireNewAsync(owner, request.TenantId, cancellationToken);
        var participant = await participants.AuthorizeActiveAsync(request.ServiceSessionId, participantToken, cancellationToken);
        RequireClean();
        CustomerTablePlanPreparedDto prepared;
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken))
        {
            var reviewed = await plans.ReviewGuestAsync(request.ServiceSessionId, participantToken, shared, cancellationToken);
            RequireReview(reviewed, currency);
            var guestOperationId = Guid.NewGuid();
            var binding = new CustomerTablePlanBinding(request.ServiceSessionId, reviewed.TableId, participant.ActorId,
                tokenHash, guestOperationId, request.ExpectedAccountRevision, reviewed.TotalMinor, currency,
                reviewed.RoundingIncrementMinor, CustomerBasketOperationHash.Payload(reviewed.Scope),
                AccountPaymentRequestRules.PlanHash(request.ServiceSessionId, SharedRequest(request, guestOperationId)));
            prepared = new(1, proofs.Create(request, owner, binding), request.ServiceSessionId,
                request.ExpectedAccountRevision, reviewed.TotalMinor, currency, reviewed.RoundingIncrementMinor,
                reviewed.Scope, reviewed.SharesMinor);
            await transaction.CommitAsync(cancellationToken);
        }
        context.ChangeTracker.Clear();
        if (currency != await access.RequireNewAsync(owner, request.TenantId, cancellationToken)
            || participant != await participants.AuthorizeActiveAsync(request.ServiceSessionId, participantToken, cancellationToken))
            throw Unavailable();
        return prepared;
    }

    public Task<CustomerTablePlanResolutionDto> SubmitAsync(string sessionId, string? participantToken,
        CustomerTablePlanSubmissionDto request, CancellationToken cancellationToken)
    {
        if (request.Intent is null || request.Proof is null)
            throw new BadRequestException("The original split intent and proof are required.");
        return RunAsync(sessionId, request.Proof, request.Intent, participantToken, false, cancellationToken);
    }
    public Task<CustomerTablePlanResolutionDto> ResolveAsync(string sessionId, CustomerTablePlanProofDto proof,
        bool stop, CancellationToken cancellationToken) => RunAsync(sessionId, proof, null, null, stop, cancellationToken);

    private async Task<CustomerTablePlanResolutionDto> RunAsync(string sessionId, CustomerTablePlanProofDto proof,
        CustomerTablePlanIntentDto? intent, string? token, bool stop, CancellationToken cancellationToken)
    {
        if (proof is null) throw new BadRequestException("The original split proof is required.");
        var owner = access.Capture(sessionId, proof.SchemaVersion, proof.TenantId);
        var binding = proofs.Require(proof, owner);
        RequireClean();
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await TableServiceSessionRowLock.LoadAsync(context, binding.ServiceSessionId, cancellationToken);
            if (owner.CustomerId is { } customer)
                await CustomerAccountWriteGuard.AcquireAsync(context, customer, true, cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({CustomerTablePlanProofs.Purpose + proof.OperationId.ToString("N")}, 0))", cancellationToken);
            RequireOwner(sessionId, proof, owner);
            if (intent is not null) proofs.RequireInput(intent, owner, binding, proof);
            var result = await decisions.ReadAsync(owner, proof, binding, cancellationToken);
            if (result is null && intent is null && !stop)
                result = new(1, proof.OperationId, binding.ServiceSessionId, "unseen", null);
            else if (result is null)
            {
                Guid? planId = null;
                if (intent is not null)
                {
                    await RequireCurrentAsync(owner, proof, binding, token, cancellationToken);
                    var shared = SharedRequest(intent, binding.GuestOperationId);
                    var reviewed = await plans.ReviewGuestAsync(binding.ServiceSessionId, token, shared, cancellationToken);
                    RequireReview(reviewed, binding.Currency);
                    if (reviewed.TotalMinor != binding.TotalMinor || reviewed.TableId != binding.TableId
                        || reviewed.RoundingIncrementMinor != binding.RoundingIncrementMinor
                        || CustomerBasketOperationHash.Payload(reviewed.Scope) != binding.ScopeHash)
                        throw new ConflictException("The reviewed split changed. Refresh the account before creating a plan.");
                    var created = await plans.CreateGuestAsync(binding.ServiceSessionId, token, shared, cancellationToken);
                    plans.RequireOnlineShares(reviewed);
                    planId = created.PlanId;
                }
                context.Set<CustomerTablePlanOperation>().Add(new()
                {
                    OperationId = proof.OperationId,
                    OwnerHash = owner.OwnerHash,
                    PayloadHash = proof.PayloadHash,
                    PlanId = planId,
                    IsStopped = stop,
                    CompletedAt = clock.GetUtcNow().UtcDateTime
                });
                await context.SaveChangesAsync(cancellationToken);
                result = await decisions.ReadAsync(owner, proof, binding, cancellationToken)
                    ?? throw new ConflictException("The original split decision was not retained.");
                if (intent is not null) await RequireCurrentAsync(owner, proof, binding, token, cancellationToken);
            }
            RequireOwner(sessionId, proof, owner);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    private async Task RequireCurrentAsync(CustomerTablePaymentOwner owner, CustomerTablePlanProofDto proof,
        CustomerTablePlanBinding binding, string? token, CancellationToken cancellationToken)
    {
        if (!TableGuestCredentialCrypto.TryHashParticipantToken(token, out var tokenHash)
            || tokenHash != binding.ParticipantTokenHash
            || binding.Currency != await access.RequireNewAsync(owner, proof.TenantId, cancellationToken)
            || (await participants.AuthorizeActiveAsync(binding.ServiceSessionId, token, cancellationToken)).ActorId != binding.ParticipantId
            || !await context.TableServiceSessions.AnyAsync(row => row.Id == binding.ServiceSessionId
                && row.TableId == binding.TableId, cancellationToken)) throw Unavailable();
    }
    private void RequireClean()
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new ConflictException("Native split decisions require an isolated clean context.");
    }
    private void RequireOwner(string sessionId, CustomerTablePlanProofDto proof, CustomerTablePaymentOwner owner)
    { if (access.Capture(sessionId, proof.SchemaVersion, proof.TenantId) != owner) throw Unavailable(); }
    private static void RequireReview(AccountSharePlanReviewDto review, string currency)
    {
        if (review.Currency != currency || review.TotalMinor > CustomerTablePaymentHashes.MaximumExactInteger
            || review.Scope.Count > CustomerTablePaymentHashes.MaximumSegments
            || review.Scope.Any(row => row.AmountMinor > CustomerTablePaymentHashes.MaximumExactInteger))
            throw new ConflictException("Refresh a supported table account before reviewing the split.");
    }
    private CreateAccountEqualSharePlanRequest SharedRequest(CustomerTablePlanIntentDto request, Guid operationId)
    {
        if (request.SchemaVersion != 1 || request.TenantId == Guid.Empty || request.OperationId == Guid.Empty
            || request.ServiceSessionId == Guid.Empty || request.ExpectedAccountRevision is < 1 or > CustomerTablePaymentHashes.MaximumExactInteger
            || request.ShareCount < 2 || request.ShareCount > limits.Value.MaximumNativePlanShares
            || request.CustomAmountsMinor is null || request.CustomAmountsMinor.Count > limits.Value.MaximumNativePlanShares)
            throw new BadRequestException("Review a supported current table split.");
        return new()
        {
            OperationId = operationId,
            ExpectedAccountRevision = request.ExpectedAccountRevision,
            ShareCount = request.ShareCount,
            CustomAmountsMinor = request.CustomAmountsMinor,
            SupersedesPlanId = request.SupersedesPlanId
        };
    }
    private static NotFoundException Unavailable() => new("Customer table split operation unavailable.");
}
