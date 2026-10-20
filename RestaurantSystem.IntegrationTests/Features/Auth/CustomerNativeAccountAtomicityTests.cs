using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Commands.CompleteNativePasswordResetCommand;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class CustomerNativeAccountAtomicityTests(DatabaseFixture fixture) : CustomerNativeAccountFixture(fixture)
{
    private readonly RevocationFailure _failure = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(_failure));
    }

    [Fact]
    public async Task A_real_revocation_save_failure_rolls_back_and_detaches_every_change_before_the_same_scope_saves_again()
    {
        var user = await CustomerAsync(confirmed: true);
        var proof = await SendLinkAsync(user, "recover");
        var before = await ReadUserAsync(user.Id);
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sessions = scope.ServiceProvider.GetRequiredService<IRefreshSessionService>();
        var refresh = await sessions.IssueAsync(user, default);
        _failure.Armed = true;
        var command = new CompleteNativePasswordResetCommand(1, proof, NewPassword, NewPassword);
        var reset = () => scope.ServiceProvider.GetRequiredService<CustomMediator>().SendCommand(command);
        await reset.Should().ThrowAsync<IOException>();
        _failure.Triggered.Should().BeTrue("the real revoker must have mutated its tracked credential before failing SaveChanges");
        context.ChangeTracker.Entries().Should().BeEmpty();
        await context.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var after = (await users.FindByIdAsync(user.Id.ToString()))!;
        after.SecurityStamp.Should().Be(before.SecurityStamp);
        after.UpdatedAt.Should().Be(before.UpdatedAt);
        (await users.CheckPasswordAsync(after, Password)).Should().BeTrue();
        (await users.CheckPasswordAsync(after, NewPassword)).Should().BeFalse();
        (await sessions.RotateAsync(after, refresh, default)).Should().NotBeNull();
        LegacyMail.Invocations.Should().BeEmpty("no change notification precedes commit");
    }

    private sealed class RevocationFailure : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<RefreshSession>()
                .Any(entry => entry.State == EntityState.Modified && entry.Entity.RevokedAt is not null))
            {
                Armed = false;
                Triggered = true;
                throw new IOException("Revocation persistence interrupted after tracked mutation");
            }
            return ValueTask.FromResult(result);
        }
    }
}
