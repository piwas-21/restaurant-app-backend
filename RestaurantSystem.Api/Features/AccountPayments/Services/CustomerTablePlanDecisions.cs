using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTablePlanDecisions(ApplicationDbContext context, TimeProvider clock) : ICustomerTablePlanDecisions
{
    public async Task RecordAsync(CustomerTablePaymentOwner owner, CustomerTablePlanProofDto proof,
        Guid? planId, bool stop, CancellationToken cancellationToken)
    {
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
    }

    public async Task<CustomerTablePlanResolutionDto?> ReadAsync(CustomerTablePaymentOwner owner, CustomerTablePlanProofDto proof,
        CustomerTablePlanBinding binding, CancellationToken cancellationToken)
    {
        var receipt = await context.Set<CustomerTablePlanOperation>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.OperationId == proof.OperationId, cancellationToken);
        if (receipt is null) return null;
        if (receipt.OwnerHash != owner.OwnerHash || receipt.PayloadHash != proof.PayloadHash) throw Unavailable();
        if (receipt.IsStopped && receipt.PlanId is null)
            return new(1, proof.OperationId, binding.ServiceSessionId, "stopped", null);
        var plan = await context.AccountEqualSharePlans.AsNoTracking().SingleOrDefaultAsync(row => row.Id == receipt.PlanId
            && row.OperationId == binding.GuestOperationId && row.ServiceSessionId == binding.ServiceSessionId
            && row.ActorId == binding.ParticipantId && row.ActorKind == AccountPaymentActorKind.GuestParticipant
            && row.AccountRevision == binding.AccountRevision && row.TotalMinor == binding.TotalMinor
            && row.Currency == binding.Currency && row.PayloadHash == binding.PlanPayloadHash && row.RoundingIncrementMinor == binding.RoundingIncrementMinor, cancellationToken)
            ?? throw new ConflictException("The original split requires reconciliation. Do not replace it.");
        var snapshot = AccountPaymentSnapshots.ToPlan(plan);
        var original = new CreateAccountEqualSharePlanRequest
        {
            OperationId = plan.OperationId,
            ExpectedAccountRevision = plan.AccountRevision,
            ShareCount = plan.ShareCount,
            CustomAmountsMinor = snapshot.CustomAmountsMinor ?? [],
            SupersedesPlanId = plan.SupersedesPlanId
        };
        if (AccountPaymentRequestRules.PlanHash(binding.ServiceSessionId, original) != binding.PlanPayloadHash
            || CustomerBasketOperationHash.Payload(snapshot.Scope) != binding.ScopeHash)
            throw new ConflictException("The original split allocation requires reconciliation. Do not replace it.");
        return new(1, proof.OperationId, binding.ServiceSessionId, "applied", plan.Id);
    }
    private static NotFoundException Unavailable() => new("Customer table split operation unavailable.");
}
