using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public static class NativeSocialAccountGuard
{
    public static async Task<ApplicationUser> AcquireAsync(ApplicationDbContext context, Guid owner, CancellationToken cancellationToken)
    {
        await CustomerAccountWriteGuard.AcquireAsync(context, owner, true, cancellationToken);
        foreach (var entry in context.ChangeTracker.Entries<ApplicationUser>().Where(entry => entry.Entity.Id == owner).ToArray())
            await entry.ReloadAsync(cancellationToken);
        return await CustomerAccountAccess.ForOwner(context, owner).SingleAsync(cancellationToken);
    }
}
