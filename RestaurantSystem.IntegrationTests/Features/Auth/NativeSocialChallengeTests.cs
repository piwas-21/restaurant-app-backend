using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class NativeSocialChallengeTests(DatabaseFixture fixture) : CustomerNativeAccountFixture(fixture)
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.Configure<CustomerNativeSocialSettings>(settings =>
        {
            settings.GoogleEnabled = true; settings.GoogleClientIds = ["shared-native-client"];
        });
        services.Configure<IdentityOptions>(options => options.Lockout.MaxFailedAccessAttempts = 2);
        services.AddScoped<INativeSocialAccess, NativeSocialAccess>();
        services.AddScoped<INativeSocialChallenges, NativeSocialChallenges>();
    }
    private async Task<NativeSocialChallengeDto> CreateAsync(Guid? owner = null, string? password = null)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<INativeSocialChallenges>()
            .CreateAsync("google", owner, password, default);
    }

    [Fact]
    public async Task AnonymousChallengePersistsOnlyItsDigestAndIndependentNonceWithoutIssuingAnySession()
    {
        var result = await CreateAsync();
        await using var context = DatabaseFixture.CreateContext();
        var row = await context.CustomerSocialChallenges.SingleAsync();
        Assert.Equal(result.ChallengeId, row.Id); Assert.Equal(result.Nonce, row.Nonce);
        Assert.Equal(Guid.Parse("11111111-1111-4111-8111-111111111111"), row.TenantId);
        Assert.Equal("login", row.Flow); Assert.Equal("google", row.Provider);
        Assert.Null(row.UserId); Assert.Null(row.SecurityStampHash); Assert.Null(row.ConsumedAt);
        Assert.True(NativeSocialChallengeProof.Matches(result.Proof, row.ProofHash));
        Assert.NotEqual(row.Nonce, result.Proof); Assert.NotEqual(row.ProofHash, result.Proof);
        Assert.Equal(TimeSpan.FromMinutes(3), row.ExpiresAt - row.CreatedAt);
        Assert.Empty(await context.RefreshSessions.ToArrayAsync());
    }

    [Fact]
    public async Task LinkingRequiresCurrentPasswordAndBindsTheOriginalCustomerSecurityStamp()
    {
        var customer = await CustomerAsync(confirmed: true);
        var result = await CreateAsync(customer.Id, Password);
        await using var context = DatabaseFixture.CreateContext();
        var row = await context.CustomerSocialChallenges.SingleAsync();
        var user = await context.Users.SingleAsync(user => user.Id == customer.Id);
        Assert.Equal("link", row.Flow); Assert.Equal(customer.Id, row.UserId);
        Assert.NotNull(user.SecurityStamp);
        Assert.Equal(NativeSocialChallengeProof.Hash(user.SecurityStamp), row.SecurityStampHash);
        Assert.NotEqual(user.SecurityStamp, row.SecurityStampHash);
        Assert.Equal(result.ChallengeId, row.Id);
        Assert.Empty(await context.RefreshSessions.ToArrayAsync());
    }

    [Theory]
    [InlineData(UserRole.Customer, false, false)]
    [InlineData(UserRole.Customer, true, true)]
    [InlineData(UserRole.Admin, true, false)]
    public async Task WithdrawsUnconfirmedPendingDeletionAndNonCustomerOwners(UserRole role, bool confirmed, bool deletion)
    {
        var customer = await CustomerAsync(role, confirmed);
        if (deletion) await ChangeUserAsync(customer.Id, user => user.DeletionScheduledAt = DateTime.UtcNow.AddDays(30));
        if (role == UserRole.Customer)
            await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(customer.Id, Password));
        else
            await Assert.ThrowsAsync<NotFoundException>(() => CreateAsync(customer.Id, Password));
        await using var context = DatabaseFixture.CreateContext();
        Assert.Empty(await context.CustomerSocialChallenges.ToArrayAsync());
        Assert.Empty(await context.RefreshSessions.ToArrayAsync());
    }

    [Fact]
    public async Task WrongReauthenticationCountsAttemptsAndLockoutWithoutCreatingAChallenge()
    {
        var customer = await CustomerAsync(confirmed: true);
        await ChangeUserAsync(customer.Id, user => user.LockoutEnabled = true);
        await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(customer.Id, "incorrect"));
        Assert.Equal(1, (await ReadUserAsync(customer.Id)).AccessFailedCount);
        await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(customer.Id, "incorrect"));
        Assert.True((await ReadUserAsync(customer.Id)).LockoutEnd > DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(customer.Id, Password));
        await using var context = DatabaseFixture.CreateContext();
        Assert.Empty(await context.CustomerSocialChallenges.ToArrayAsync());
        Assert.Empty(await context.RefreshSessions.ToArrayAsync());
    }

    [Fact]
    public async Task SuccessfulReauthenticationClearsTheFailedCounterInTheChallengeTransaction()
    {
        var customer = await CustomerAsync(confirmed: true);
        await ChangeUserAsync(customer.Id, user => user.AccessFailedCount = 1);
        await CreateAsync(customer.Id, Password);
        Assert.Equal(0, (await ReadUserAsync(customer.Id)).AccessFailedCount);
    }

    [Fact]
    public async Task RefusesPendingUnrelatedContextEditsWithoutPublishingOrDiscardingThem()
    {
        var customer = await CustomerAsync(confirmed: true);
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tracked = await context.Users.SingleAsync(user => user.Id == customer.Id);
        tracked.FirstName = "Uncommitted";
        await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<INativeSocialChallenges>()
            .CreateAsync("google", customer.Id, Password, default));
        Assert.Equal("Uncommitted", tracked.FirstName);
        Assert.True(context.ChangeTracker.HasChanges());
        Assert.Equal("Native", (await ReadUserAsync(customer.Id)).FirstName);
    }

    [Fact]
    public async Task ErasureCascadesPrivateBindingsAndChallengesWithoutLeavingAReactivationKey()
    {
        var customer = await CustomerAsync(confirmed: true);
        await CreateAsync(customer.Id, Password);
        await using var context = DatabaseFixture.CreateContext();
        context.CustomerSocialIdentities.Add(new CustomerSocialIdentity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111"),
            UserId = customer.Id,
            Provider = "google",
            Issuer = NativeGoogleIdentityVerifier.Issuer,
            Subject = "original-subject",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        await context.Users.Where(user => user.Id == customer.Id).ExecuteDeleteAsync();
        Assert.Empty(await context.CustomerSocialChallenges.ToArrayAsync());
        Assert.Empty(await context.CustomerSocialIdentities.ToArrayAsync());
    }
}
