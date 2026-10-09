using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 1")]
public sealed class IsolatedHostResetSafetyTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<TenantFeatureSettings>(settings => settings.OptionSetMaterializationEnabled = true);

    [Fact]
    public async Task Customized_host_keeps_application_workers_out_of_the_reset_lane()
    {
        var hostedTypes = Factory.Services.GetServices<IHostedService>().Select(service => service.GetType()).ToArray();
        hostedTypes.Should().NotContain(typeof(OptionSetMaterializationJobWorker),
            "the worker starts immediately and can query while the lane is being reset");
        hostedTypes.Should().NotContain(type => type.Assembly == typeof(Program).Assembly);
        hostedTypes.Should().NotBeEmpty("framework hosted services must still start the HTTP test server");

        await DatabaseFixture.ResetDatabaseAsync();
        using var response = await Client.GetAsync("/api/health");
        response.IsSuccessStatusCode.Should().BeTrue("the isolated HTTP host remains usable after reset");
    }
}
