using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Preserves reviewed unit scopes when an older client tries to write money.</summary>
internal static class AccountPaymentLedgerGuard
{
    internal static async Task RequireLegacyCollectionAsync(
        ApplicationDbContext context, Guid serviceSessionId, CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        var states = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId)
            .Select(value => value.State).ToListAsync(cancellationToken);
        if (states.Any(value => value == AccountPaymentState.Captured || value.HoldsReservation()))
            throw new ConflictException("Use the account payment flow to preserve paid and reserved item scopes.");
    }

    internal static async Task<TableServiceSession?> LockOrderAccountAsync(
        ApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        var membership = await context.Orders.AsNoTracking()
            .Where(value => value.Id == orderId).Select(value => value.ServiceSessionId)
            .SingleOrDefaultAsync(cancellationToken);
        var session = membership.HasValue
            ? await TableServiceSessionRowLock.LoadAsync(context, membership.Value, cancellationToken)
                ?? throw new ConflictException("The order's table account is missing.")
            : null;
        var current = await context.Orders.AsNoTracking()
            .Where(value => value.Id == orderId).Select(value => value.ServiceSessionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (membership != current)
            throw new ConflictException("The order moved to a table account. Refresh before changing its payment.");
        return session;
    }

    internal static async Task RequireNoPendingAsync(
        ApplicationDbContext context, Guid serviceSessionId, CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        var states = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId)
            .Select(value => value.State).ToListAsync(cancellationToken);
        if (states.Any(value => value.HoldsReservation()))
            throw new ConflictException("Resolve reserved or processing account payments before closing the visit.");
    }

    internal static async Task RequireOrderCorrectionAsync(
        ApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        if (await HasProtectedOrderScopeAsync(context, orderId, cancellationToken))
            throw new ConflictException("Resolve the order's reserved or allocated account payment before cancelling its items.");
    }

    internal static async Task<bool> HasProtectedOrderScopeAsync(
        ApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        var states = await context.AccountPaymentAllocations.AsNoTracking()
            .Where(value => value.OrderId == orderId).Select(value => value.Attempt!.State)
            .ToListAsync(cancellationToken);
        return states.Any(value => value == AccountPaymentState.Captured || value.HoldsReservation());
    }

    private static void RequireTransaction(ApplicationDbContext context)
    {
        if (context.Database.CurrentTransaction is null)
            throw new ConflictException("Account payment writes require a locked transaction.");
    }
}
