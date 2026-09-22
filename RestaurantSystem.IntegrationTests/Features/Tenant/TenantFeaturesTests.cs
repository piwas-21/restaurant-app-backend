using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Tenant;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Tenant;

public class TenantFeaturesTests
{
    [Fact]
    public void The_rollout_defaults_to_false()
    {
        var features = Create(new TenantFeatureSettings());

        features.ServerWorkspaceV2.Should().BeFalse();
    }

    [Fact]
    public void The_rollout_can_be_enabled()
    {
        var features = Create(new TenantFeatureSettings { ServerWorkspaceV2 = true });

        features.ServerWorkspaceV2.Should().BeTrue();
    }

    [Fact]
    public void Configuration_binding_rejects_an_invalid_boolean()
    {
        var services = new ServiceCollection();
        services.AddOptions<TenantFeatureSettings>()
            .Bind(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{TenantFeatureSettings.SectionName}:ServerWorkspaceV2"] = "maybe",
                })
                .Build()
                .GetSection(TenantFeatureSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<TenantFeatureSettings>>().Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Anonymous_endpoint_publishes_the_current_rollout(bool enabled)
    {
        var controller = new TenantFeaturesController(Create(new TenantFeatureSettings
        {
            ServerWorkspaceV2 = enabled,
        }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var response = controller.Get().Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = response.Value.Should().BeOfType<ApiResponse<TenantFeaturesDto>>().Subject;

        body.Success.Should().BeTrue();
        body.Data!.ServerWorkspaceV2.Should().Be(enabled);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    private static TenantFeatures Create(TenantFeatureSettings settings) =>
        new(Options.Create(settings));
}
