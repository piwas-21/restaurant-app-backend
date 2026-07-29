using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// <see cref="RequireModuleAttribute"/> — the endpoint half of module runtime
/// enforcement (sofra ADR-010 / S11, SOFRA-ONBOARDING-PLAN O5).
///
/// The contract worth pinning is the SHAPE of the denial: 404 (not 403, which would
/// advertise a feature the tenant never bought) carrying
/// <see cref="ErrorCodes.ModuleNotEnabled"/>, which is the only thing that tells the
/// frontend "no reservations module here" apart from "no such reservation".
/// </summary>
public class RequireModuleAttributeTests
{
    private static AuthorizationFilterContext ContextFor(string enabled, bool enforce)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantModules>(new TenantModules(
            Options.Create(new ModuleSettings { Enabled = enabled, Enforce = enforce }),
            NullLogger<TenantModules>.Instance));

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    [Fact]
    public void A_disabled_module_answers_404_with_the_ModuleNotEnabled_code()
    {
        var context = ContextFor("core,cashier", enforce: true);

        new RequireModuleAttribute(ModuleIds.Reservations).OnAuthorization(context);

        var result = context.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var body = result.Value.Should().BeOfType<ApiResponse<object>>().Subject;
        body.Success.Should().BeFalse();
        body.ErrorCode.Should().Be(ErrorCodes.ModuleNotEnabled);
    }

    [Fact]
    public void An_enabled_module_leaves_the_pipeline_alone()
    {
        var context = ContextFor("core,reservations", enforce: true);

        new RequireModuleAttribute(ModuleIds.Reservations).OnAuthorization(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void Nothing_is_blocked_while_the_instance_is_unrestricted()
    {
        // The RUMI case again, this time through the filter: no list, so every gate is open
        // regardless of which module it names.
        var context = ContextFor("", enforce: true);

        new RequireModuleAttribute(ModuleIds.Loyalty).OnAuthorization(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void A_gate_naming_an_unrecognised_module_denies_rather_than_opens()
    {
        // Fail closed: a typo in a gate must not hand out the endpoint.
        var context = ContextFor("core,reservations", enforce: true);

        new RequireModuleAttribute("reservatons").OnAuthorization(context);

        context.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void The_attribute_refuses_to_be_constructed_without_a_module(string moduleId)
    {
        var construct = () => new RequireModuleAttribute(moduleId);

        construct.Should().Throw<ArgumentException>();
    }
}
