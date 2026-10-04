using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderAmendmentResolutionSettingsTests
{
    [Fact]
    public void Production_registration_binds_the_unchanged_bounded_defaults()
    {
        using var provider = BuildProvider();

        var settings = provider.GetRequiredService<IOptions<OrderAmendmentResolutionSettings>>().Value;

        settings.AmendmentQuoteLifetimeMinutes.Should().Be(5);
        settings.FinancialResolutionQuoteLifetimeMinutes.Should().Be(2);
        settings.ProviderRefundPageSize.Should().Be(100);
        settings.MaximumProviderRefundPages.Should().Be(10);
        settings.ProviderIdempotencySafetyWindowHours.Should().Be(23);
    }

    [Theory]
    [InlineData(nameof(OrderAmendmentResolutionSettings.AmendmentQuoteLifetimeMinutes), "0")]
    [InlineData(nameof(OrderAmendmentResolutionSettings.AmendmentQuoteLifetimeMinutes), "11")]
    [InlineData(nameof(OrderAmendmentResolutionSettings.FinancialResolutionQuoteLifetimeMinutes), "0")]
    [InlineData(nameof(OrderAmendmentResolutionSettings.FinancialResolutionQuoteLifetimeMinutes), "11")]
    [InlineData(nameof(OrderAmendmentResolutionSettings.ProviderRefundPageSize), "0")]
    [InlineData(nameof(OrderAmendmentResolutionSettings.ProviderRefundPageSize), "101")]
    [InlineData(nameof(OrderAmendmentResolutionSettings.MaximumProviderRefundPages), "0")]
    [InlineData(nameof(OrderAmendmentResolutionSettings.MaximumProviderRefundPages), "11")]
    [InlineData(nameof(OrderAmendmentResolutionSettings.ProviderIdempotencySafetyWindowHours), "0")]
    [InlineData(nameof(OrderAmendmentResolutionSettings.ProviderIdempotencySafetyWindowHours), "24")]
    public void Production_registration_rejects_values_outside_the_configured_bounds(
        string propertyName, string configuredValue)
    {
        using var provider = BuildProvider(propertyName, configuredValue);

        var read = () => provider.GetRequiredService<IOptions<OrderAmendmentResolutionSettings>>().Value;

        read.Should().Throw<OptionsValidationException>();
    }

    private static ServiceProvider BuildProvider(string? propertyName = null, string? configuredValue = null)
    {
        var values = new Dictionary<string, string?>();
        if (propertyName is not null && configuredValue is not null)
            values[$"{OrderAmendmentResolutionSettings.SectionName}:{propertyName}"] = configuredValue;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrderAmendmentServices();
        return services.BuildServiceProvider();
    }
}
