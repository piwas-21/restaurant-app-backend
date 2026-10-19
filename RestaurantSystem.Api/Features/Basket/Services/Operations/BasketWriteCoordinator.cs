using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

/// <summary>All basket service writers share the same owner lock and atomic save boundary.</summary>
public sealed class BasketWriteCoordinator(ApplicationDbContext context) : IBasketWriteCoordinator
{
    public Task<T> RunAsync<T>(string sessionId, Guid? userId, Func<Task<T>> work,
        CancellationToken cancellationToken = default) =>
        RunLockedAsync([CustomerBasketOperationHash.WriteOwnerLock(sessionId, userId)], work, cancellationToken);

    public Task<T> MergeAsync<T>(string sessionId, Guid userId, Func<Task<T>> work) =>
        RunLockedAsync([
            CustomerBasketOperationHash.WriteOwnerLock(sessionId, null),
            CustomerBasketOperationHash.WriteOwnerLock(sessionId, userId)
        ], work, CancellationToken.None);

    public async Task RecalculateAsync(Guid basketId, Func<Task> work)
    {
        var owner = await OwnerAsync(basketId);
        if (owner is null) return;
        await RunAsync(owner.SessionId, owner.UserId, async () =>
        {
            var current = await OwnerAsync(basketId);
            if (current is null) return false;
            if (current != owner) throw new ConflictException("Basket ownership changed. Refresh your basket.");
            await work();
            return true;
        });
    }

    private Task<Owner?> OwnerAsync(Guid basketId) => context.Baskets.AsNoTracking()
        .Where(basket => basket.Id == basketId)
        .Select(basket => new Owner(basket.SessionId, basket.UserId)).SingleOrDefaultAsync();

    private async Task<T> RunLockedAsync<T>(IReadOnlyList<long> locks, Func<Task<T>> work,
        CancellationToken cancellationToken)
    {
        // Native operations already own the transaction. Reenter their transaction-scoped lock
        // without committing/disposing that outer scope. Legacy calls own a new atomic boundary.
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        foreach (var key in locks.Distinct().Order())
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
        try
        {
            var result = await work();
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (transaction is not null)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                finally
                {
                    // Login intentionally continues after a failed merge. An unrelated later
                    // SaveChanges must not persist this aborted basket's tracked mutations.
                    foreach (var entry in context.ChangeTracker.Entries()
                        .Where(entry => entry.Entity is RestaurantSystem.Domain.Entities.Basket or BasketItem).ToArray())
                        entry.State = EntityState.Detached;
                }
            }
            throw;
        }
    }

    private sealed record Owner(string SessionId, Guid? UserId);
}
