using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CashierPrintSettingsTests
{
    [Theory]
    [InlineData(null, 3)]
    [InlineData("5", 5)]
    public void Snapshot_retry_budget_uses_default_or_configured_value(string? attempts, int expected)
    {
        using var provider = Provider(attempts);
        provider.GetRequiredService<IOptions<CashierPrintSettings>>().Value.MaximumSnapshotAttempts.Should().Be(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    public void Invalid_snapshot_retry_budgets_are_refused(string attempts)
    {
        using var provider = Provider(attempts);
        var read = () => provider.GetRequiredService<IOptions<CashierPrintSettings>>().Value;
        read.Should().Throw<OptionsValidationException>();
    }

    private static ServiceProvider Provider(string? attempts)
    {
        var values = new Dictionary<string, string?>();
        if (attempts is not null)
            values[$"{CashierPrintSettings.SectionName}:MaximumSnapshotAttempts"] = attempts;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrderDetailServices();
        return services.BuildServiceProvider();
    }
}
