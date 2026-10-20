using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CurrentStaffTicketSettingsTests
{
    [Fact]
    public void Ticket_budgets_bind_from_configuration()
    {
        using var provider = Provider("20", "30");
        var settings = provider.GetRequiredService<IOptions<CurrentStaffTicketSettings>>().Value;
        settings.MaximumMembers.Should().Be(20);
        settings.MaximumAmendments.Should().Be(30);
    }

    [Theory]
    [InlineData("0", "1000")]
    [InlineData("100", "0")]
    [InlineData("1001", "1000")]
    [InlineData("100", "10001")]
    public void Invalid_ticket_budgets_are_refused(string members, string amendments)
    {
        using var provider = Provider(members, amendments);
        var read = () => provider.GetRequiredService<IOptions<CurrentStaffTicketSettings>>().Value;
        read.Should().Throw<OptionsValidationException>();
    }

    private static ServiceProvider Provider(string members, string amendments)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{CurrentStaffTicketSettings.SectionName}:MaximumMembers"] = members,
            [$"{CurrentStaffTicketSettings.SectionName}:MaximumAmendments"] = amendments
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrderDetailServices();
        return services.BuildServiceProvider();
    }
}
