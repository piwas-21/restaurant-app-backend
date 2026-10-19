using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

public sealed class CustomerOrderOperationStore(ApplicationDbContext context, IBasketWriteCoordinator coordinator,
    ICurrentUserService currentUser, IHttpContextAccessor httpContext) : ICustomerOrderOperationStore
{
    public Task<CustomerOrderOperationResolution> ExecuteAsync(string sessionId, Guid operationId, string payloadHash,
        Func<Task<Guid>> accept, CancellationToken cancellationToken) =>
        RunAsync(sessionId, operationId, payloadHash, async previous =>
        {
            if (previous is not null)
            {
                if (previous.IsCancelled) throw new ConflictException("This order operation was cancelled. Review your basket.");
                return Resolution(previous);
            }
            var orderId = await accept();
            if (orderId == Guid.Empty || !await context.Orders.AsNoTracking()
                .AnyAsync(order => order.Id == orderId && order.UserId == currentUser.UserId, cancellationToken))
                throw new InvalidOperationException("Acceptance must save an order for the current owner in this transaction.");
            context.Set<CustomerOrderOperation>().Add(Receipt(sessionId, operationId, payloadHash, orderId));
            await context.SaveChangesAsync(cancellationToken);
            return new(CustomerOrderOperationOutcome.Applied, orderId);
        }, cancellationToken);

    public Task<CustomerOrderOperationResolution> RecoverAsync(string sessionId, Guid operationId, string payloadHash,
        CancellationToken cancellationToken) =>
        RunAsync(sessionId, operationId, payloadHash, previous => Task.FromResult(previous is null
            ? new CustomerOrderOperationResolution(CustomerOrderOperationOutcome.Unseen, null)
            : Resolution(previous)), cancellationToken);

    public Task<CustomerOrderOperationResolution> CancelAsync(string sessionId, Guid operationId, string payloadHash,
        CancellationToken cancellationToken) =>
        RunAsync(sessionId, operationId, payloadHash, async previous =>
        {
            if (previous is not null) return Resolution(previous);
            var cancelled = Receipt(sessionId, operationId, payloadHash, null);
            context.Set<CustomerOrderOperation>().Add(cancelled);
            await context.SaveChangesAsync(cancellationToken);
            return Resolution(cancelled);
        }, cancellationToken);

    private async Task<CustomerOrderOperationResolution> RunAsync(string sessionId, Guid operationId, string payloadHash,
        Func<CustomerOrderOperation?, Task<CustomerOrderOperationResolution>> work, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Order operation stores must own their transaction.");
        if (context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Order operations require a clean change tracker.");
        CustomerBasketAccess.EnsureCustomerOrGuest(currentUser, httpContext.HttpContext);
        var session = CustomerBasketOperationHash.Session(sessionId);
        if (currentUser.IsAuthenticated && currentUser.UserId is null)
            throw new UnauthorizedAccessException("Renew customer authentication before placing an order.");
        if (operationId == Guid.Empty || payloadHash.Length != 64 || payloadHash.Any(value => !char.IsAsciiHexDigit(value)))
            throw new BadRequestException("A non-empty operation ID and canonical payload hash are required.");
        var owner = CustomerBasketOperationHash.Owner(session, currentUser.UserId);
        try
        {
            return await coordinator.RunAsync(sessionId, currentUser.UserId, async () =>
            {
                var key = CustomerBasketOperationHash.OrderOperationLock(operationId);
                await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
                var previous = await context.Set<CustomerOrderOperation>().AsNoTracking()
                    .SingleOrDefaultAsync(operation => operation.OperationId == operationId, cancellationToken);
                if (previous is not null)
                {
                    if (!string.Equals(previous.OwnerHash, owner, StringComparison.Ordinal))
                        throw new NotFoundException("Order operation unavailable.");
                    if (!string.Equals(previous.PayloadHash, payloadHash, StringComparison.Ordinal))
                        throw new ConflictException("This operation ID was used for different order details.");
                }
                return await work(previous);
            }, cancellationToken);
        }
        catch
        {
            // Rollback alone does not undo EF state. A later save must never resurrect an aborted
            // order, financial graph or cancellation outside the operation transaction/locks.
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private CustomerOrderOperation Receipt(string sessionId, Guid operationId, string payloadHash, Guid? orderId) => new()
    {
        OperationId = operationId,
        OwnerHash = CustomerBasketOperationHash.Owner(CustomerBasketOperationHash.Session(sessionId), currentUser.UserId),
        PayloadHash = payloadHash,
        OrderId = orderId,
        IsCancelled = !orderId.HasValue,
        CompletedAt = DateTime.UtcNow
    };

    private static CustomerOrderOperationResolution Resolution(CustomerOrderOperation operation) =>
        new(operation.IsCancelled ? CustomerOrderOperationOutcome.Cancelled : CustomerOrderOperationOutcome.Applied,
            operation.OrderId);
}
