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
        { "OrdersController", "GetZReport", ModuleIds.Cashier },
    };

    [Theory]
    [MemberData(nameof(GatedControllers))]
    public void The_controller_carries_its_module_gate(string controller, string moduleId)
    {
        var gate = Controller(controller).GetCustomAttribute<RequireModuleAttribute>();

        gate.Should().NotBeNull($"{controller} belongs to the {moduleId} module");
        gate!.ModuleIdsRequired.Should().Equal(moduleId);
    }

    [Theory]
    [MemberData(nameof(GatedActions))]
    public void The_action_carries_its_module_gate(string controller, string action, string moduleId)
    {
        var method = Controller(controller).GetMethod(action)
            ?? throw new InvalidOperationException($"No action {controller}.{action}");

        var gate = method.GetCustomAttribute<RequireModuleAttribute>();

        gate.Should().NotBeNull($"{controller}.{action} is the {moduleId} module's own surface");
        gate!.ModuleIdsRequired.Should().Equal(moduleId);
    }

    [Fact]
    public void The_shared_service_stream_is_reachable_with_EITHER_till_module()
    {
        // GET /api/Events/service feeds BOTH the cashier till and the server floor view
        // (frontend useCashierOrdersStream + serverOrdersSseHandlers both point at it).
        // Gated on `server` alone, a cashier-without-server tenant got a till that renders
        // perfectly and never receives an order — silent, which is the worst kind.
        var gate = Controller("EventsController").GetMethod("ServiceEvents")!
            .GetCustomAttribute<RequireModuleAttribute>();

        gate.Should().NotBeNull();
        gate!.ModuleIdsRequired.Should().BeEquivalentTo(new[] { ModuleIds.Server, ModuleIds.Cashier });
    }

    [Fact]
    public void Every_gate_in_the_assembly_names_a_module_the_catalog_knows()
    {
        // A gate naming an unrecognised id fails CLOSED, i.e. a typo removes a feature from
        // every enforcing tenant and nothing else complains. Catch it at build time instead.
        var gates = Api.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<RequireModuleAttribute>()
                .Concat(t.GetMethods().SelectMany(m => m.GetCustomAttributes<RequireModuleAttribute>())))
            .SelectMany(a => a.ModuleIdsRequired)
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
            .SelectMany(a => a.ModuleIdsRequired)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // `online-payments` is exempt ONLY until S4 lands. It is in the vocabulary now so a
        // registry entry carrying it is recognised and provisioning accepts it (S10), but the
        // surface it gates — POST /api/payments/checkout-session — does not exist yet. When S4
        // adds PaymentsController with [RequireModule(ModuleIds.OnlinePayments)], DELETE this
        // entry: leaving it would re-open exactly the hole this test exists to close, on the one
        // module where the unenforced surface is a money path.
        //
        // The exemption is safe in the meantime for a reason that is not "we'll remember": nothing
        // can buy it yet either. There is no endpoint to leave ungated.
        var notYetBuilt = new[] { ModuleIds.OnlinePayments };

        var owed = ModuleIds.All
            .Except(new[] { ModuleIds.Core, ModuleIds.ExtraLanguages })
            .Except(notYetBuilt)
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
