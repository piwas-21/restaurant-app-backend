using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.User.Services;

/// <summary>Serializes account mutations with erasure while permitting unrelated foreign-key checks.</summary>
public static class CustomerAccountWriteGuard
{
    public static async Task AcquireAsync(ApplicationDbContext context, Guid owner, bool requireCustomer,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Account write guards require an active transaction.");
        var owners = await context.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM "Users"
            WHERE id = {owner} AND is_deleted = false FOR NO KEY UPDATE
            """).ToArrayAsync(cancellationToken);
        if (owners.Length != 1 || (requireCustomer && !await CustomerAccountAccess.ForOwner(context, owner)
                .AsNoTracking().AnyAsync(cancellationToken)))
            throw new NotFoundException("Account unavailable.");
    }
}
