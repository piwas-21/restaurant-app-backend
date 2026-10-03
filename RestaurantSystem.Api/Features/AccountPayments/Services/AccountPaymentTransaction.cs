using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Owns a short database transaction without nesting an existing command transaction.</summary>
internal sealed class AccountPaymentTransaction : IAsyncDisposable
{
    private IDbContextTransaction? _transaction;

    private AccountPaymentTransaction(IDbContextTransaction? transaction) => _transaction = transaction;

    internal static async Task<AccountPaymentTransaction> BeginAsync(
        ApplicationDbContext context, CancellationToken cancellationToken) =>
        new(context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null);

    internal async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null) return;
        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public ValueTask DisposeAsync() => _transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
}
