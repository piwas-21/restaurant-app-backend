using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

/// <summary>One original private proof and current database owner in each locked phase.</summary>
public sealed class CustomerOrderCheckoutOwnerScope(ApplicationDbContext context, ICustomerCheckoutGuard guard,
    ICustomerOrderOperationStore operations, TimeProvider clock) : ICustomerOrderCheckoutOwnerScope
{
    public async Task<T> RunAsync<T>(string sessionId, CustomerOrderRecoveryDto proof,
        Func<CustomerOrderCheckoutJournal, Order, DateTime, Task<T>> work, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Native payment phases require an isolated clean context.");
        if (proof.SchemaVersion != 1) throw new BadRequestException("Unsupported native payment schema.");
        var owner = guard.EnsureOwner(sessionId);
        var operation = await operations.RecoverAsync(sessionId, proof.OperationId, proof.PayloadHash, cancellationToken);
        if (operation.Outcome != CustomerOrderOperationOutcome.Applied || operation.OrderId is not { } orderId)
            throw Unavailable();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var orderLock = await OrderAccountMutationScope.BeginAsync(context, orderId, cancellationToken);
            if (owner is { } userId)
                await CustomerAccountWriteGuard.AcquireAsync(context, userId, true, cancellationToken);
            var order = await context.Orders.AsNoTracking().SingleOrDefaultAsync(value => value.Id == orderId
                && value.UserId == owner && !value.IsDeleted, cancellationToken) ?? throw Unavailable();
            if (order.ServiceSessionId is not null) throw Unavailable();
            var journal = await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.OrderId == orderId && value.OperationId == proof.OperationId,
                    cancellationToken) ?? throw Unavailable();
            var result = await work(journal, order, clock.GetUtcNow().UtcDateTime);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static NotFoundException Unavailable() => new("Native payment operation unavailable.");
}
