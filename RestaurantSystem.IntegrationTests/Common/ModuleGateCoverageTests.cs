using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Modules;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// Asserts that the v1 module gates are actually ATTACHED (SOFRA-ONBOARDING-PLAN O5).
///
/// TenantModulesTests proves the decision logic and RequireModuleAttributeTests proves the
/// denial shape — but both stay green if someone deletes an attribute, at which point a
/// tenant silently gets back a module they did not buy. Enforcement that can be removed
/// without a test going red is not enforcement, so the wiring is pinned here.
/// </summary>
public class ModuleGateCoverageTests
{
    private static readonly Assembly Api = typeof(RequireModuleAttribute).Assembly;

    private static Type Controller(string name) =>
        Api.GetTypes().SingleOrDefault(t => t.Name == name)
        ?? throw new InvalidOperationException($"No controller named {name} in the API assembly");

    public static TheoryData<string, string> GatedControllers() => new()
    {
        { "ReservationsController", ModuleIds.Reservations },
        { "ReservationQuickActionsController", ModuleIds.Reservations },
        { "FidelityPointsController", ModuleIds.Loyalty },
        { "FidelityAnalyticsController", ModuleIds.Loyalty },
        { "PointRulesController", ModuleIds.Loyalty },
        { "CustomerDiscountsController", ModuleIds.Loyalty },
        { "UserGroupController", ModuleIds.Loyalty },
        { "GroupDiscountController", ModuleIds.Loyalty },
        { "PrinterFeedController", ModuleIds.Printing },
        { "DevicesController", ModuleIds.Printing },
    };

    public static TheoryData<string, string, string> GatedActions() => new()
    {
        { "EventsController", "KitchenEvents", ModuleIds.KitchenBoard },
        { "EventsController", "ServiceEvents", ModuleIds.Server },
        { "OrdersController", "GetZReport", ModuleIds.Cashier },
    };

    [Theory]
    [MemberData(nameof(GatedControllers))]
    public void The_controller_carries_its_module_gate(string controller, string moduleId)
    {
        var gate = Controller(controller).GetCustomAttribute<RequireModuleAttribute>();

        gate.Should().NotBeNull($"{controller} belongs to the {moduleId} module");
        gate!.ModuleId.Should().Be(moduleId);
    }

    [Theory]
    [MemberData(nameof(GatedActions))]
    public void The_action_carries_its_module_gate(string controller, string action, string moduleId)
    {
        var method = Controller(controller).GetMethod(action)
            ?? throw new InvalidOperationException($"No action {controller}.{action}");

        var gate = method.GetCustomAttribute<RequireModuleAttribute>();

        gate.Should().NotBeNull($"{controller}.{action} is the {moduleId} module's own surface");
        gate!.ModuleId.Should().Be(moduleId);
    }

    [Fact]
    public void Every_gate_in_the_assembly_names_a_module_the_catalog_knows()
    {
        // A gate naming an unrecognised id fails CLOSED, i.e. a typo removes a feature from
        // every enforcing tenant and nothing else complains. Catch it at build time instead.
        var gates = Api.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<RequireModuleAttribute>()
                .Concat(t.GetMethods().SelectMany(m => m.GetCustomAttributes<RequireModuleAttribute>())))
            .Select(a => a.ModuleId)
            .Distinct()
            .ToArray();

        gates.Should().NotBeEmpty();
        gates.Should().OnlyContain(id => ModuleIds.IsKnown(id));
    }

    [Fact]
    public void Every_paid_module_is_enforced_somewhere()
    {
        // The point of O5: a module you can buy but nothing enforces is a price for a product
        // that does not vary. `core` is never gated (it is the instance), and
        // `extra-languages` is a pricing rule rather than a surface.
        var gated = Api.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<RequireModuleAttribute>()
                .Concat(t.GetMethods().SelectMany(m => m.GetCustomAttributes<RequireModuleAttribute>())))
            .Select(a => a.ModuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var owed = ModuleIds.All
            .Except(new[] { ModuleIds.Core, ModuleIds.ExtraLanguages })
            .Where(id => !gated.Contains(id))
            .ToArray();

        owed.Should().BeEmpty("every sellable surface module needs at least one real gate");
    }

    /// <summary>
    /// Surfaces the plan deliberately leaves UNGATED because their endpoints are shared with a
    /// core surface. Adding a gate here would break every tenant that bought the *other* module
    /// — reservations needs the table map as much as `server` does — with no test going red,
    /// which is the same argument this file makes in the other direction.
    /// </summary>
    public static TheoryData<string> DeliberatelyUngatedControllers() => new()
    {
        "TablesController",
        "FloorPlanController",
    };

    [Theory]
    [MemberData(nameof(DeliberatelyUngatedControllers))]
    public void The_shared_controllers_stay_ungated(string controller)
    {
        Controller(controller).GetCustomAttribute<RequireModuleAttribute>()
            .Should().BeNull($"{controller} is shared across modules — see SOFRA-ONBOARDING-PLAN O5");
    }

    [Theory]
    [InlineData("OrdersController")]
    [InlineData("EventsController")]
    public void The_shared_order_controllers_are_gated_per_action_never_wholesale(string controller)
    {
        // A class-level gate here would take core order handling away from a tenant that simply
        // did not buy the cashier or kitchen-board add-on.
        Controller(controller).GetCustomAttribute<RequireModuleAttribute>().Should().BeNull();
    }

    [Fact]
    public void The_gated_types_are_controllers()
    {
        // Guards the reflection above against a rename that silently matches something else.
        foreach (var name in GatedControllers().Select(row => (string)row[0]!))
        {
            Controller(name).Should().BeAssignableTo<ControllerBase>();
        }
    }
}
