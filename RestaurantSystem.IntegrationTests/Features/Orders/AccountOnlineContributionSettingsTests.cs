using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountOnlineContributionSettingsTests
{
    [Theory]
    [InlineData("CHF", 49)]
    [InlineData("GBP", 29)]
    [InlineData("AED", 50)]
    [InlineData("TRY", 1000)]
    public void Production_registration_rejects_unsupported_currency_or_below_provider_floor(
        string currency, long minimum)
    {
        using var provider = BuildProvider(currency, minimum);
        var resolve = () => provider.GetRequiredService<IOptions<AccountOnlineContributionSettings>>().Value;
        resolve.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData("CHF", 50)]
    [InlineData("GBP", 30)]
    [InlineData("AED", 200)]
    public void Production_registration_accepts_inert_default_or_supported_provider_boundary(
        string? currency, long minimum)
    {
        using var provider = BuildProvider(currency, minimum);
        var settings = provider.GetRequiredService<IOptions<AccountOnlineContributionSettings>>().Value;
        settings.SettlementCurrency.Should().Be(currency);
        settings.MinimumAmountMinor.Should().Be(minimum);
    }

    private static ServiceProvider BuildProvider(string? currency, long minimum)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AccountOnlineContribution:SettlementCurrency"] = currency,
            ["AccountOnlineContribution:MinimumAmountMinor"] = minimum.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAccountPaymentServices();
        return services.BuildServiceProvider();
    }
}
