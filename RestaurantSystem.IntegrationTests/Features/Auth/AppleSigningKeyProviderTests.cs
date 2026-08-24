using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Features.Auth.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

/// <summary>
/// BACKEND-NOTES §4.1 asks for Apple's keys to be "CACHED and refreshed (do not fetch per
/// request)". A per-request fetch would put an outbound HTTPS call on the critical path of every
/// sign-in and hand an anonymous caller a way to make us hammer Apple, so the cache is a
/// behaviour worth asserting rather than an implementation detail.
/// </summary>
public class AppleSigningKeyProviderTests
{
    [Fact]
    public async Task Keys_AreFetchedOnce_AndReusedByLaterRequests()
    {
        var (provider, fetcher, _) = Build();

        await provider.GetSigningKeysAsync(forceRefresh: false, CancellationToken.None);
        var second = await provider.GetSigningKeysAsync(forceRefresh: false, CancellationToken.None);

        fetcher.Calls.Should().Be(1, "the second login must be served from the cache");
        second.Should().ContainSingle();
    }

    [Fact]
    public async Task CachedKeys_AreRefetchedOnceTheCacheWindowHasPassed()
    {
        var (provider, fetcher, clock) = Build(new AppleAuthSettings { JwksCacheMinutes = 30 });

        await provider.GetSigningKeysAsync(forceRefresh: false, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(31));
        await provider.GetSigningKeysAsync(forceRefresh: false, CancellationToken.None);

        fetcher.Calls.Should().Be(2);
    }

    /// <summary>
    /// A token naming an unknown <c>kid</c> forces a refresh — but anyone can send one, so the
    /// second forced refresh inside the floor must be served from cache instead of calling Apple.
    /// </summary>
    [Fact]
    public async Task ForcedRefresh_IsFloored_SoAnUnknownKidCannotBeUsedToHammerApple()
    {
        var (provider, fetcher, clock) = Build();

        await provider.GetSigningKeysAsync(forceRefresh: false, CancellationToken.None);
        await provider.GetSigningKeysAsync(forceRefresh: true, CancellationToken.None);
        await provider.GetSigningKeysAsync(forceRefresh: true, CancellationToken.None);

        fetcher.Calls.Should().Be(1, "the floor is 5 minutes and no time has passed");

        clock.Advance(TimeSpan.FromMinutes(6));
        await provider.GetSigningKeysAsync(forceRefresh: true, CancellationToken.None);

        fetcher.Calls.Should().Be(2, "past the floor a rotation must be picked up");
    }

    /// <summary>
    /// Apple being briefly unreachable must not lock every Apple user out while a usable key set
    /// is still in hand — and with no key set at all the caller gets nothing, which fails closed
    /// in the validator's signature check.
    /// </summary>
    [Fact]
    public async Task AFailedFetch_KeepsServingTheCachedKeys()
    {
        var (provider, fetcher, clock) = Build(new AppleAuthSettings { JwksCacheMinutes = 1 });

        await provider.GetSigningKeysAsync(forceRefresh: false, CancellationToken.None);
        fetcher.Throw = true;
        clock.Advance(TimeSpan.FromMinutes(5));

        var keys = await provider.GetSigningKeysAsync(forceRefresh: false, CancellationToken.None);

        keys.Should().ContainSingle("a stale key set beats no key set");
        fetcher.Calls.Should().Be(2, "it did try");
    }

    [Fact]
    public async Task AFirstFetchThatFails_YieldsNoKeys_SoValidationFailsClosed()
    {
        var (provider, fetcher, _) = Build();
        fetcher.Throw = true;

        var keys = await provider.GetSigningKeysAsync(forceRefresh: false, CancellationToken.None);

        keys.Should().BeEmpty();
    }

    private static (AppleSigningKeyProvider Provider, RecordingFetcher Fetcher, TestClock Clock) Build(
        AppleAuthSettings? settings = null)
    {
        var fetcher = new RecordingFetcher();
        var services = new ServiceCollection();
        services.AddSingleton<IAppleJwksFetcher>(fetcher);
        var clock = new TestClock();

        var provider = new AppleSigningKeyProvider(
            services.BuildServiceProvider(),
            Options.Create(settings ?? new AppleAuthSettings()),
            NullLogger<AppleSigningKeyProvider>.Instance,
            clock);

        return (provider, fetcher, clock);
    }

    private sealed class RecordingFetcher : IAppleJwksFetcher
    {
        public int Calls { get; private set; }

        public bool Throw { get; set; }

        public Task<JsonWebKeySet> FetchAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw)
            {
                throw new HttpRequestException("apple is down");
            }

            var keySet = new JsonWebKeySet();
            keySet.Keys.Add(JsonWebKeyConverter.ConvertFromSecurityKey(AppleTestTokens.PublicKey));
            return Task.FromResult(keySet);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
