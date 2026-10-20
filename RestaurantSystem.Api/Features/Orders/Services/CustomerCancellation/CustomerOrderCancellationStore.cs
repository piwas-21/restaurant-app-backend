using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

/// <summary>Atomic versioned cancellation and decision receipt; each phase owns its transaction.</summary>
public sealed class CustomerOrderCancellationStore(ApplicationDbContext context, ICustomerOrderCancellationRules rules,
    ICurrentUserService currentUser, TimeProvider clock) : ICustomerOrderCancellationStore
{
    private const string CancellationReason = "Customer declined preparation delay";

    public Task<CustomerOrderCancellationPreparationDto> PrepareAsync(CustomerOrderCancellationOwner owner,
        CancellationToken cancellationToken) => WithOrderAsync(owner, null, async order =>
        {
            var reason = await rules.RefusalUnderLockAsync(order, cancellationToken);
            return new CustomerOrderCancellationPreparationDto(1, order.Id, order.Version, reason is null,
                reason, reason is null ? CustomerOrderCancellationHash.Payload(order.Id, order.Version) : null);
        }, cancellationToken);

    public Task<CustomerOrderCancellationWriteResult> SubmitAsync(CustomerOrderCancellationOwner owner,
        CustomerOrderCancellationIntentDto intent, CancellationToken cancellationToken) =>
        RunAsync(owner, intent, true, cancellationToken);

    public async Task<CustomerOrderCancellationResolutionDto> RecoverAsync(CustomerOrderCancellationOwner owner,
        CustomerOrderCancellationIntentDto intent, CancellationToken cancellationToken) =>
        (await RunAsync(owner, intent, false, cancellationToken)).Resolution;

    private Task<CustomerOrderCancellationWriteResult> RunAsync(CustomerOrderCancellationOwner owner,
        CustomerOrderCancellationIntentDto intent, bool submit, CancellationToken cancellationToken)
    {
        CustomerOrderCancellationHash.Validate(intent);
        if (intent.OrderId != owner.OrderId) throw Unavailable();
        return WithOrderAsync<CustomerOrderCancellationWriteResult>(owner, intent.OperationId, async order =>
        {
            var previous = await context.Set<CustomerOrderCancellationOperation>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.OperationId == intent.OperationId, cancellationToken);
            if (previous is not null)
            {
                VerifyReplay(previous, owner, intent);
                return new(Resolution(previous), false);
            }
            if (!submit) return new(new(1, intent.OperationId, order.Id, CustomerOrderCancellationOutcome.Unseen, null, null), false);
            var reason = order.Version != intent.ExpectedVersion ? CustomerOrderCancellationReason.OrderChanged
                : await rules.RefusalUnderLockAsync(order, cancellationToken);
            var now = clock.GetUtcNow().UtcDateTime;
            if (reason is null) await CancelAsync(order, now, cancellationToken);
            var operation = new CustomerOrderCancellationOperation
            {
                OperationId = intent.OperationId,
                OrderId = order.Id,
                OwnerHash = owner.OwnerHash,
                PayloadHash = intent.PayloadHash,
                ExpectedVersion = intent.ExpectedVersion,
                ObservedVersion = order.Version,
                Outcome = reason is null ? CustomerOrderCancellationOutcome.Cancelled : CustomerOrderCancellationOutcome.Refused,
                Reason = reason,
                CompletedAt = now
            };
            context.Set<CustomerOrderCancellationOperation>().Add(operation);
            await context.SaveChangesAsync(cancellationToken);
            return new(Resolution(operation), reason is null);
        }, cancellationToken);
    }

    private async Task CancelAsync(Order order, DateTime now, CancellationToken cancellationToken)
    {
        var actor = currentUser.GetAuditIdentifier();
        context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = order.Status,
            ToStatus = OrderStatus.Cancelled,
            Notes = CancellationReason,
            ChangedAt = now,
            ChangedBy = actor,
            CreatedAt = now,
            CreatedBy = actor
        });
        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = CancellationReason;
        order.UpdatedAt = now;
        order.UpdatedBy = actor;
        // The common audit hook increments the tracked order version and change sequence.
        // The resulting receipt is saved afterward inside this same transaction.
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<T> WithOrderAsync<T>(CustomerOrderCancellationOwner owner, Guid? operationId,
        Func<Order, Task<T>> work, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Cancellation phases require an isolated clean context.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (operationId is { } id)
            {
                var key = CustomerBasketOperationHash.CancellationOperationLock(id);
                await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
            }
            await using var orderLock = await OrderAccountMutationScope.BeginAsync(context, owner.OrderId, cancellationToken);
            if (owner.UserId is { } userId)
                await CustomerAccountWriteGuard.AcquireAsync(context, userId, true, cancellationToken);
            var order = await context.Orders.SingleOrDefaultAsync(value => value.Id == owner.OrderId
                && value.UserId == owner.UserId && !value.IsDeleted, cancellationToken) ?? throw Unavailable();
            // A scoped context may have observed this row earlier; identity resolution is not
            // fresh version/ownership evidence. Reload while the shared order lock is held.
            await context.Entry(order).ReloadAsync(cancellationToken);
            if (order.UserId != owner.UserId || order.IsDeleted) throw Unavailable();
            if (order.Version <= 0) throw new ConflictException("The order version needs restaurant review.");
            var result = await work(order);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private static void VerifyReplay(CustomerOrderCancellationOperation operation, CustomerOrderCancellationOwner owner,
        CustomerOrderCancellationIntentDto intent)
    {
        if (operation.OrderId != owner.OrderId || !string.Equals(operation.OwnerHash, owner.OwnerHash, StringComparison.Ordinal))
            throw Unavailable();
        if (operation.ExpectedVersion != intent.ExpectedVersion
            || !string.Equals(operation.PayloadHash, intent.PayloadHash, StringComparison.Ordinal))
            throw new ConflictException("This cancellation operation was used for different details.");
    }

    private static CustomerOrderCancellationResolutionDto Resolution(CustomerOrderCancellationOperation operation) =>
        new(1, operation.OperationId, operation.OrderId, operation.Outcome, operation.ObservedVersion, operation.Reason);
    private static NotFoundException Unavailable() => new("Order cancellation unavailable.");
}
