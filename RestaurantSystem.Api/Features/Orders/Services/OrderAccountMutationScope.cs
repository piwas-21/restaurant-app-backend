using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Serializes one native mutation with visit membership before locking its order.</summary>
public sealed class OrderAccountMutationScope : IAsyncDisposable
{
    private IDbContextTransaction? _transaction;
    private readonly TableServiceSession? _session;

    private OrderAccountMutationScope(IDbContextTransaction? transaction, TableServiceSession? session)
    {
        _transaction = transaction;
        _session = session;
    }

    public static async Task<OrderAccountMutationScope> BeginAsync(
        ApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var membership = await context.Orders.AsNoTracking()
                .Where(order => order.Id == orderId)
                .Select(order => new { order.ServiceSessionId })
                .SingleOrDefaultAsync(cancellationToken);
            var session = membership?.ServiceSessionId is Guid sessionId
                ? await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken) : null;
            if (membership?.ServiceSessionId is not null && session is null)
                throw new ConflictException("The table account is unavailable. Refresh before changing the order.");

            var locked = await context.Orders
                .FromSqlInterpolated($"SELECT * FROM orders WHERE id = {orderId} FOR UPDATE")
                .AsNoTracking().Select(order => new { order.ServiceSessionId })
                .SingleOrDefaultAsync(cancellationToken);
            if (locked?.ServiceSessionId != membership?.ServiceSessionId)
                throw new ConflictException("The order changed table accounts. Refresh before changing it.");
            return new OrderAccountMutationScope(transaction, session);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            throw;
        }
    }

    public void RecordAccountChange() => _session?.RecordAccountChange();

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null) await _transaction.DisposeAsync();
    }
}
