using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public sealed class CustomerTableRoundReceipts(ApplicationDbContext context, TimeProvider clock) : ICustomerTableRoundReceipts
{
    public Task LockAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var key = CustomerBasketOperationHash.OrderOperationLock(operationId);
        return context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }
    public async Task RequireUnusedAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (await context.Set<CustomerOrderOperation>().AsNoTracking()
                .AnyAsync(value => value.OperationId == operationId, cancellationToken))
            throw new ConflictException("This round attempt already has a decision. Recover the original attempt.");
    }
    public async Task<CustomerTableRoundResolutionDto?> ReadAsync(CustomerTableRoundOwner owner,
        CustomerTableRoundProofDto proof, CustomerTableRoundBinding binding, CancellationToken cancellationToken)
    {
        var receipt = await context.Set<CustomerOrderOperation>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OperationId == proof.OperationId, cancellationToken);
        if (receipt is not null && (receipt.OwnerHash != owner.OwnerHash || receipt.PayloadHash != proof.PayloadHash))
            throw Unavailable();
        var round = await context.Set<TableGuestRoundOperation>().AsNoTracking().SingleOrDefaultAsync(value =>
            value.ServiceSessionId == binding.ServiceSessionId && value.OperationId == binding.GuestOperationId, cancellationToken);
        if (round is not null) RequireRound(round, owner, binding);
        if (receipt is null)
        {
            // The two receipts commit atomically. Partial evidence cannot authorize another
            // round or certify a stopped attempt after an inconsistent restore or deletion.
            if (round is not null) throw Unavailable();
            return null;
        }
        if (receipt.IsCancelled)
        {
            if (round is not null || receipt.OrderId is not null) throw Unavailable();
            return Resolution(proof, binding, null);
        }
        if (round is null || receipt.OrderId != round.OrderId) throw Unavailable();
        return Resolution(proof, binding, round.OrderId);
    }
    public async Task<CustomerTableRoundResolutionDto> SaveAsync(CustomerTableRoundOwner owner,
        CustomerTableRoundProofDto proof, CustomerTableRoundBinding binding, Guid? orderId, CancellationToken cancellationToken)
    {
        context.Set<CustomerOrderOperation>().Add(new CustomerOrderOperation
        {
            OperationId = proof.OperationId,
            OwnerHash = owner.OwnerHash,
            PayloadHash = proof.PayloadHash,
            OrderId = orderId,
            IsCancelled = !orderId.HasValue,
            CompletedAt = clock.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync(cancellationToken);
        return Resolution(proof, binding, orderId);
    }
    private static void RequireRound(TableGuestRoundOperation round, CustomerTableRoundOwner owner, CustomerTableRoundBinding binding)
    {
        var expected = TableGuestCredentialCrypto.HashRoundRequest(binding.ServiceSessionId, binding.ParticipantId,
            binding.GuestOperationId, binding.ExpectedAccountRevision,
            TableGuestCredentialCrypto.HashBasketSession(owner.BasketSessionId.ToString("D")), binding.BasketFingerprint);
        if (round.ParticipantId != binding.ParticipantId || round.OrderId == Guid.Empty || round.RequestHash != expected)
            throw Unavailable();
    }
    private static CustomerTableRoundResolutionDto Resolution(CustomerTableRoundProofDto proof,
        CustomerTableRoundBinding binding, Guid? orderId) => new(1, proof.OperationId, binding.ServiceSessionId,
        orderId.HasValue ? CustomerOrderOperationOutcome.Applied : CustomerOrderOperationOutcome.Cancelled, orderId);
    private static NotFoundException Unavailable() => new("Customer table round operation unavailable.");
}
