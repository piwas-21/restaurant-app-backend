using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal sealed record LockedAccountCheckout(
    TableServiceSession Session, AccountPaymentAttempt Attempt, AccountCheckoutJournal Journal);

/// <summary>Every provider write uses the same visit, attempt, journal lock order as collection.</summary>
internal static class AccountCheckoutLocks
{
    internal static async Task<LockedAccountCheckout> LoadAsync(ApplicationDbContext context,
        Guid attemptId, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new ConflictException("Provider evidence requires a locked money transaction.");
        var sessionId = await context.AccountPaymentAttempts.AsNoTracking().Where(value => value.Id == attemptId)
            .Select(value => value.ServiceSessionId).SingleOrDefaultAsync(cancellationToken);
        var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
            ?? throw Unavailable();
        await context.Entry(session).ReloadAsync(cancellationToken);
        var attempt = await context.AccountPaymentAttempts
            .FromSqlInterpolated($"SELECT * FROM account_payment_attempts WHERE id = {attemptId} FOR UPDATE")
            .Include(value => value.Allocations).SingleOrDefaultAsync(cancellationToken) ?? throw Unavailable();
        await context.Entry(attempt).ReloadAsync(cancellationToken);
        foreach (var allocation in attempt.Allocations)
            await context.Entry(allocation).ReloadAsync(cancellationToken);
        var journal = await context.AccountCheckoutJournals
            .FromSqlInterpolated($"SELECT * FROM account_checkout_journals WHERE attempt_id = {attemptId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw Unavailable();
        await context.Entry(journal).ReloadAsync(cancellationToken);
        if (attempt.ServiceSessionId != session.Id || journal.AttemptId != attempt.Id)
            throw Unavailable();
        return new(session, attempt, journal);
    }

    private static NotFoundException Unavailable() => new("The original provider contribution is unavailable.");
}
