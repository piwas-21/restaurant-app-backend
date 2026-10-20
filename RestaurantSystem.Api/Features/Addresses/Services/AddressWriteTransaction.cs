using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Api.Features.User.Services;

namespace RestaurantSystem.Api.Features.Addresses.Services;

/// <summary>Serializes owned address writes before any address is read or changed.</summary>
public static class AddressWriteTransaction
{
    public static async Task<IDbContextTransaction> BeginAsync(ApplicationDbContext context,
        Guid owner, CancellationToken cancellationToken, bool requireCustomer = false)
    {
        if (context.Database.CurrentTransaction is not null)
            throw new ConflictException("Address changes require their own transaction.");
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // NO KEY UPDATE serializes address writers and cooperates with erasure's user lock,
            // while allowing unrelated order FK checks to acquire KEY SHARE on the account.
            await CustomerAccountWriteGuard.AcquireAsync(context, owner, requireCustomer, cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
