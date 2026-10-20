using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

public abstract class NativeSocialFixture(DatabaseFixture fixture) : CustomerNativeAccountFixture(fixture)
{
    protected static readonly Guid Tenant = Guid.Parse("11111111-1111-4111-8111-111111111111");
    protected readonly Mock<INativeGoogleIdentityVerifier> Verifier = new();
    protected NativeSocialIdentity Identity = new("google", NativeGoogleIdentityVerifier.Issuer,
        "original-subject", "shared-native-client", "provider@example.test", true);

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.Configure<CustomerNativeSocialSettings>(settings =>
        {
            settings.GoogleEnabled = true;
            settings.GoogleClientIds = ["shared-native-client"];
        });
        services.AddScoped<INativeSocialChallenges, NativeSocialChallenges>();
        services.AddScoped<INativeSocialAccounts, NativeSocialAccounts>();
        services.AddScoped<INativeSocialCompletion, NativeSocialCompletion>();
        services.AddScoped<INativeSocialAccess, NativeSocialAccess>();
        Verifier.Setup(value => value.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(Identity));
        services.AddSingleton(Verifier.Object);
    }
    protected async Task<NativeSocialChallengeDto> ChallengeAsync(Guid? owner = null)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<INativeSocialChallenges>()
            .CreateAsync("google", owner, owner is null ? null : Password, default);
    }
    protected async Task<AuthResponse?> CompleteAsync(NativeSocialChallengeDto challenge, Guid? owner = null)
    {
        using var scope = Factory.Services.CreateScope();
        return await CompleteInScopeAsync(scope.ServiceProvider, challenge, owner);
    }
    protected static Task<AuthResponse?> CompleteInScopeAsync(IServiceProvider services,
        NativeSocialChallengeDto challenge, Guid? owner) => services.GetRequiredService<INativeSocialCompletion>()
        .CompleteAsync(challenge.ChallengeId, challenge.Proof, "validated-provider-token", owner,
            new NativeSocialProfile("Provider", "Customer"), default);

    protected static async Task<int> PrepareConnectionAsync(ApplicationDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        context.Database.SetCommandTimeout(TimeSpan.FromSeconds(8));
        return ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
    }
    protected async Task WaitForLockAsync(int pid)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var observer = DatabaseFixture.CreateContext();
        while (!await observer.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock') AS "Value"
            """).SingleAsync(deadline.Token))
            await Task.Delay(10, deadline.Token);
    }
    protected static async Task LockErasureAsync(ApplicationDbContext context, Guid user)
    {
        // The real scrubber holds User before the cleanup worker's cascading DELETE.
        await new RetainedCustomerDataScrubber(context).ScrubAsync(user, default);
    }
}
