using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class ProviderOrderTimestampTests
{
    [Theory]
    [InlineData("2026-10-02T01:00:00Z")]
    [InlineData("2026-10-02T03:00:00+02:00")]
    [InlineData("2026-10-01T20:00:00-05:00")]
    [InlineData("2026-10-02T01:00:00.0000000Z")]
    public void ExplicitOffsetsIdentifyTheSameInstant(string value)
    {
        var expected = new DateTimeOffset(2026, 10, 2, 1, 0, 0, TimeSpan.Zero);
        Assert.True(ProviderOrderTimestamp.TryRead(value, expected, out var timestamp));
        Assert.Equal(expected, timestamp);
    }

    [Theory]
    [InlineData("2026-10-02T01:00:00")]
    [InlineData("2026-10-02T01:00:00.0000001Z")]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("10/02/2026T01:00:00Z")]
    [InlineData("2026-10-02T01:00:00+25:00")]
    public void InvalidOrFutureInstantsAreRefused(string value)
        => Assert.False(ProviderOrderTimestamp.TryRead(value,
            new DateTimeOffset(2026, 10, 2, 1, 0, 0, TimeSpan.Zero), out _));
}
