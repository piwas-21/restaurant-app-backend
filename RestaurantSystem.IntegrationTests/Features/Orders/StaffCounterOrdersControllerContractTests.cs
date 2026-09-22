using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders;
using RestaurantSystem.Api.Features.Orders.Models;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>Locks the split staff-order routes and the optional printer device binding.</summary>
public sealed class StaffCounterOrdersControllerContractTests
{
    [Fact]
    public void Counter_commands_keep_their_existing_routes_when_routing_is_split_out()
    {
        var controller = typeof(StaffCounterOrdersController);

        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/staff/orders");
        controller.GetMethod(nameof(StaffCounterOrdersController.Quote))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("quote");
        controller.GetMethod(nameof(StaffCounterOrdersController.Create))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template.Should().BeNull();
        controller.GetMethod(nameof(StaffCounterOrdersController.Release))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("{orderId:guid}/release");
        controller.GetMethod("Routing").Should().BeNull();
    }

    [Fact]
    public void Routing_projection_owns_the_original_get_route_and_staff_gates()
    {
        var controller = typeof(StaffOrderRoutingController);

        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/staff/orders");
        controller.GetCustomAttributes<AuthorizeAttribute>().Should().NotBeEmpty();
        controller.GetCustomAttribute<RequireTableServiceStaffAttribute>().Should().NotBeNull();
        controller.GetCustomAttribute<RequireModuleAttribute>()!.ModuleIdsRequired
            .Should().BeEquivalentTo(ModuleIds.Server, ModuleIds.Cashier);
        controller.GetMethod(nameof(StaffOrderRoutingController.Routing))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{orderId:guid}/routing");
    }

    [Fact]
    public void Printer_feed_uses_model_binding_for_the_optional_device_header()
    {
        var parameter = typeof(PrinterFeedController).GetMethod(nameof(PrinterFeedController.Get))!
            .GetParameters().Single(item => item.Name == "deviceHeader");
        var binding = parameter.GetCustomAttribute<ModelBinderAttribute>();

        parameter.ParameterType.Should().Be<OptionalDeviceHeader>();
        binding.Should().NotBeNull();
        binding!.Name.Should().Be("X-Device-Id");
        binding.BinderType.Should().Be<OptionalDeviceHeaderModelBinder>();
    }
}
