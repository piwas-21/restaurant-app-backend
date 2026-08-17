using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.User;

/// <summary>
/// GDPR Art. 17 erasure must actually erase.
/// </summary>
/// <remarks>
/// This command had NO coverage, which is how a change that turned it into a no-op passed a fully
/// green 581-test suite. It is the one deletion path that used <c>UserManager.DeleteAsync</c>, and
/// that calls <c>_context.Remove(user)</c> internally — so once the soft-delete interceptor started
/// running on async saves, the erasure would have flagged the row instead of removing it, leaving
/// every piece of PII in place. <c>ApplicationUser</c> is <c>IExcludeFromGlobalFilter</c>, so the
/// row would not even have been hidden, and the unfiltered unique index on the email would have
/// burned that address permanently.
/// </remarks>
[Collection("Database Lane 1")]
public class ConfirmAccountDeletionCommandTests : IntegrationTestBase
{
    public ConfirmAccountDeletionCommandTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private async Task<(ApplicationUser user, string token)> CreateDeletableUserAsync(IServiceScope scope)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"erasure-{Guid.NewGuid():N}@test.local",
            Email = $"erasure-{Guid.NewGuid():N}@test.local",
            FirstName = "Erasure",
            LastName = "Test",
            Role = UserRole.Customer,
            RefreshToken = string.Empty,
            CreatedBy = "test"
        };

        var created = await userManager.CreateAsync(user, "Str0ng!Passw0rd");
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));

        var token = await userManager.GenerateUserTokenAsync(user, "Default", "AccountDeletion");
        return (user, token);
    }

    [Fact]
    public async Task ConfirmedDeletion_RemovesTheRow_RatherThanFlaggingIt()
    {
        using var scope = Factory.Services.CreateScope();
        var (user, token) = await CreateDeletableUserAsync(scope);

        var result = await ConfirmDeletionAsync(user.Id, token);
        Assert.True(result.Success, result.Message);

        // IgnoreQueryFilters is the whole point: a soft delete would leave a row that a filtered
        // query hides, so a filtered assertion would pass against the broken behaviour too.
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var survivor = await context.Users
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(u => u.Id == user.Id);

        Assert.Null(survivor);
    }

    [Fact]
    public async Task ConfirmedDeletion_FreesTheEmailForReRegistration()
    {
        // The user-visible consequence of getting this wrong: the unique indexes on Email /
        // NormalizedEmail are NOT filtered on is_deleted, so a surviving flagged row would make the
        // address unusable forever.
        using var scope = Factory.Services.CreateScope();
        var (user, token) = await CreateDeletableUserAsync(scope);
        var email = user.Email!;

        var result = await ConfirmDeletionAsync(user.Id, token);
        Assert.True(result.Success, result.Message);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var reRegistered = await userManager.CreateAsync(
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                FirstName = "New",
                LastName = "Owner",
                Role = UserRole.Customer,
                RefreshToken = string.Empty,
                CreatedBy = "test"
            },
            "Str0ng!Passw0rd");

        Assert.True(reRegistered.Succeeded,
            string.Join(", ", reRegistered.Errors.Select(e => e.Description)));
    }

    [Fact]
    public async Task AnInvalidToken_DeletesNothing()
    {
        using var scope = Factory.Services.CreateScope();
        var (user, _) = await CreateDeletableUserAsync(scope);

        var result = await ConfirmDeletionAsync(user.Id, "not-a-real-token");
        Assert.False(result.Success);

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stillThere = await context.Users
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(u => u.Id == user.Id);

        Assert.NotNull(stillThere);
    }

    /// <summary>
    /// Drives the real endpoint rather than the handler: the handler is dispatched through
    /// CustomMediator and is not registered in DI on its own, and going over HTTP is what every
    /// sibling command test does.
    /// </summary>
    private async Task<ApiResponse<string>> ConfirmDeletionAsync(Guid userId, string token)
    {
        var response = await PostAsJsonAsync("/api/user/confirm-deletion", new { UserId = userId, Token = token });
        response.EnsureSuccessStatusCode();
        return (await ReadResponseAsync<ApiResponse<string>>(response))!;
    }
}
