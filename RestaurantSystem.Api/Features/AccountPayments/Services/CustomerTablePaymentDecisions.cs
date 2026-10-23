using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTablePaymentDecisions(ApplicationDbContext context) : ICustomerTablePaymentDecisions
{
    public async Task<CustomerTablePaymentResolutionDto?> ReadAsync(CustomerTablePaymentOwner owner,
        CustomerTablePaymentProofDto proof, CustomerTablePaymentBinding binding, CancellationToken cancellationToken)
    {
        var receipt = await context.Set<CustomerTablePaymentOperation>().AsNoTracking().SingleOrDefaultAsync(
            row => row.OperationId == proof.OperationId, cancellationToken);
        if (receipt is null) return null;
        if (receipt.OwnerHash != owner.OwnerHash || receipt.PayloadHash != proof.PayloadHash)
            throw new NotFoundException("Customer table payment operation unavailable.");
        return await ResolutionAsync(receipt, binding, cancellationToken);
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

}
