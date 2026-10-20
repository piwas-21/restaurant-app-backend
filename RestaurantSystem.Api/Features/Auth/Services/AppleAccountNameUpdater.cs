using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Auth.Services;

/// <summary>Cosmetic failures cannot publish dirty state or upgrade a stale sign-in authority.</summary>
public sealed class AppleAccountNameUpdater(UserManager<ApplicationUser> users, ApplicationDbContext context,
    ILogger<AppleAccountNameUpdater> logger) : IAppleAccountNameUpdater
{
    public async Task UpdateAsync(ApplicationUser user, string? firstName, string? lastName)
    {
        var first = AppleAccountNameRules.Clean(firstName);
        var last = AppleAccountNameRules.Clean(lastName);
        if ((first is null || first == user.FirstName) && (last is null || last == user.LastName)) return;
        var entry = context.Entry(user);
        if (entry.State != EntityState.Unchanged || context.ChangeTracker.HasChanges())
            throw new ConflictException("The account changed. Sign in again.");
        var before = entry.CurrentValues.Clone();
        if (first is not null) user.FirstName = first;
        if (last is not null) user.LastName = last;
        var result = await users.UpdateAsync(user);
        if (result.Succeeded) return;
        // Restore this attempt only. Reloading would borrow a newer security stamp/role after
        // a concurrency refusal; the credential issuer must still compare the ORIGINAL authority.
        entry.CurrentValues.SetValues(before);
        entry.State = EntityState.Unchanged;
        logger.LogWarning("Apple name update refused for {UserId}: {Codes}", user.Id,
            string.Join(", ", result.Errors.Select(error => error.Code)));
    }
}
