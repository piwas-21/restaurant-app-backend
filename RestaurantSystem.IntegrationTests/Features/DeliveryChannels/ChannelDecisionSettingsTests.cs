using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

public sealed class ChannelDecisionSettingsTests
{
    [Theory]
    [InlineData(nameof(DeliveryChannelSettings.DecisionLeaseSeconds), 59)]
    [InlineData(nameof(DeliveryChannelSettings.DecisionLeaseSeconds), 301)]
    [InlineData(nameof(DeliveryChannelSettings.DecisionRetrySeconds), 9)]
    [InlineData(nameof(DeliveryChannelSettings.DecisionRetrySeconds), 301)]
    [InlineData(nameof(DeliveryChannelSettings.DecisionClockToleranceSeconds), -1)]
    [InlineData(nameof(DeliveryChannelSettings.DecisionClockToleranceSeconds), 61)]
    public void OutOfRangeTimingIsRejectedEvenWhenChannelDisabled(string key, int seconds)
    {
        using var services = Settings(new() { [$"DeliveryChannels:{key}"] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var read = () => services.GetRequiredService<IOptions<DeliveryChannelSettings>>().Value;
        read.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void ReviewedCustomDurationsAndDefaultsAreLoaded()
    {
        using var defaults = Settings([]);
        var original = defaults.GetRequiredService<IOptions<DeliveryChannelSettings>>().Value;
        original.DecisionLeaseSeconds.Should().Be(120);
        original.DecisionRetrySeconds.Should().Be(30);
        original.DecisionClockToleranceSeconds.Should().Be(30);
        using var custom = Settings(new()
        {
            ["DeliveryChannels:DecisionLeaseSeconds"] = "180",
            ["DeliveryChannels:DecisionRetrySeconds"] = "90",
            ["DeliveryChannels:DecisionClockToleranceSeconds"] = "2",
        });
        var configured = custom.GetRequiredService<IOptions<DeliveryChannelSettings>>().Value;
        configured.DecisionLeaseSeconds.Should().Be(180);
        configured.DecisionRetrySeconds.Should().Be(90);
        configured.DecisionClockToleranceSeconds.Should().Be(2);
    }

    private static ServiceProvider Settings(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddSingleton<IConfiguration>(configuration)
            .AddDeliveryChannelServices().BuildServiceProvider();
    }
}
