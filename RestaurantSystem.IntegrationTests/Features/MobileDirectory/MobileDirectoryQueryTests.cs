using Microsoft.Extensions.Options;
using Sofra.MobileService.Directory;

namespace RestaurantSystem.IntegrationTests.Features.MobileDirectory;

public sealed class MobileDirectoryQueryTests
{
    [Fact]
    public void SnapshotExpiryIsBoundedAndConfigurationCollectionsCannotMutatePublishedIdentity()
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var versions = new List<int> { 1 };
        var tenants = new List<PublicTenantDto> { MobileDirectoryFixture.Alpha with { ContractVersions = versions } };
        var handler = new GetMobileDirectoryQueryHandler(Options.Create(MobileDirectoryFixture.Settings(tenants)), new FixedClock(now));
        versions.Add(99); tenants.Clear();
        var result = handler.Handle();
        Assert.Equal(now.UtcDateTime, result.PublishedAt);
        Assert.Equal(now.AddHours(1).UtcDateTime, result.ExpiresAt);
        Assert.Single(result.Tenants);
        Assert.Equal([1], result.Tenants[0].ContractVersions);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)result.Tenants[0].ContractVersions).Add(2));
        Assert.Single(handler.Handle().Tenants);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
