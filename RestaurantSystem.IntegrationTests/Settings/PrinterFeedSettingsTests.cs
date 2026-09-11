using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Settings;

public class PrinterFeedSettingsTests
{
    [Fact]
    public void Update_page_size_defaults_to_fifty()
    {
        new PrinterFeedSettings().UpdatePageSize.Should().Be(50);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Update_page_size_must_fit_the_sentinel_query(int updatePageSize)
    {
        var services = new ServiceCollection();
        services.AddOptions<PrinterFeedSettings>()
            .Configure(options => options.UpdatePageSize = updatePageSize)
            .ValidateDataAnnotations();
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<PrinterFeedSettings>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }
}
