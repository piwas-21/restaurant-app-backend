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
        features.TableAccountV1.Should().BeFalse();
        features.OrderAmendmentsV1.Should().BeFalse();
        features.TableGuestVisitsV1.Should().BeFalse();
        features.TableVisitReadinessV1.Should().BeFalse();
        features.TableAccountPaymentsV1.Should().BeFalse();
        features.TableGuestAccountPaymentsV1.Should().BeFalse();
        features.EnforceSauceMinimum.Should().BeFalse();
        features.OptionSetMaterializationEnabled.Should().BeFalse();
    }

    [Fact]
    public void The_rollout_can_be_enabled()
    {
        var features = Create(new TenantFeatureSettings { ServerWorkspaceV2 = true });

        features.ServerWorkspaceV2.Should().BeTrue();
    }

    [Fact]
    public void Table_readiness_rollout_requires_server_workspace_and_guest_visit_prerequisites()
    {
        var missingWorkspace = () => Create(new TenantFeatureSettings
        {
            TableGuestVisitsV1 = true,
            TableVisitReadinessV1 = true,
        });
        var missingGuestVisits = () => Create(new TenantFeatureSettings
        {
            ServerWorkspaceV2 = true,
            TableVisitReadinessV1 = true,
        });

        missingWorkspace.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires ServerWorkspaceV2 and TableGuestVisitsV1*");
        missingGuestVisits.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires ServerWorkspaceV2 and TableGuestVisitsV1*");

        Create(new TenantFeatureSettings
        {
            ServerWorkspaceV2 = true,
            TableGuestVisitsV1 = true,
            TableVisitReadinessV1 = true,
        }).TableVisitReadinessV1.Should().BeTrue();
    }

    [Fact]
    public void Sauce_minimum_enforcement_can_be_enabled_per_tenant()
    {
        var features = Create(new TenantFeatureSettings { EnforceSauceMinimum = true });

        features.EnforceSauceMinimum.Should().BeTrue();
    }

    [Fact]
    public void Option_set_materialization_can_be_enabled_per_tenant()
    {
        var features = Create(new TenantFeatureSettings { OptionSetMaterializationEnabled = true });

        features.OptionSetMaterializationEnabled.Should().BeTrue();
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
                    [$"{TenantFeatureSettings.SectionName}:TableVisitReadinessV1"] = "maybe",
                    [$"{TenantFeatureSettings.SectionName}:EnforceSauceMinimum"] = "maybe",
                    [$"{TenantFeatureSettings.SectionName}:OptionSetMaterializationEnabled"] = "maybe",
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
            TableAccountV1 = enabled,
            OrderAmendmentsV1 = enabled,
            TableGuestVisitsV1 = enabled,
            TableVisitReadinessV1 = enabled,
            TableAccountPaymentsV1 = enabled,
            ServerAccountCollectionV1 = enabled,
            TableGuestAccountPaymentsV1 = enabled,
            EnforceSauceMinimum = enabled,
            OptionSetMaterializationEnabled = enabled,
        }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var response = controller.Get().Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = response.Value.Should().BeOfType<ApiResponse<TenantFeaturesDto>>().Subject;

        body.Success.Should().BeTrue();
        body.Data!.ServerWorkspaceV2.Should().Be(enabled);
        body.Data.TableAccountV1.Should().Be(enabled);
        body.Data.OrderAmendmentsV1.Should().Be(enabled);
        body.Data.TableGuestVisitsV1.Should().Be(enabled);
        body.Data.TableVisitReadinessV1.Should().Be(enabled);
        body.Data.TableAccountPaymentsV1.Should().Be(enabled);
        body.Data.ServerAccountCollectionV1.Should().Be(enabled);
        body.Data.TableGuestAccountPaymentsV1.Should().Be(enabled);
        body.Data.EnforceSauceMinimum.Should().Be(enabled);
        body.Data.OptionSetMaterializationEnabled.Should().Be(enabled);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    private static TenantFeatures Create(TenantFeatureSettings settings) =>
        new(Options.Create(settings));
}
