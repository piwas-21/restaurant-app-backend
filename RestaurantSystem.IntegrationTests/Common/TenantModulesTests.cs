using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// Module runtime enforcement (sofra ADR-010 / S11 — workspace SOFRA-ONBOARDING-PLAN O5).
///
/// The rules being pinned here, in precedence order:
///   1. enforcement off              -> everything on
///   2. no module list configured    -> everything on   (the LIVE RUMI case)
///   3. `core`                       -> on              (fail open)
///   4. unrecognised id              -> off             (fail closed)
///   5. otherwise                    -> on iff listed
/// </summary>
public class TenantModulesTests
{
    private static TenantModules Create(string enabled, bool enforce) =>
        new(Options.Create(new ModuleSettings { Enabled = enabled, Enforce = enforce }),
            NullLogger<TenantModules>.Instance);

    // ── Rule 1: the flag ─────────────────────────────────────────────────────
    [Theory]
    [InlineData(ModuleIds.Reservations)]
    [InlineData(ModuleIds.Loyalty)]
    [InlineData(ModuleIds.Printing)]
    public void Enforcement_off_leaves_every_module_on_even_with_a_narrow_list(string moduleId)
    {
        var modules = Create("core", enforce: false);

        modules.IsEnforced.Should().BeFalse();
        modules.IsEnabled(moduleId).Should().BeTrue();
    }

    // ── Rule 2: RUMI. The one that must never regress ────────────────────────
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    public void An_absent_module_list_means_unrestricted_not_empty(string enabled)
    {
        // The legacy RUMI install runs the main `deploy` compose project, which sets no
        // TENANT_MODULES at all. Reading "absent" as "nothing enabled" would take the whole
        // app away from the one live paying client — so absent must beat even Enforce=true.
        var modules = Create(enabled, enforce: true);

        modules.IsEnforced.Should().BeFalse();
        modules.EnabledModules.Should().BeEquivalentTo(ModuleIds.All);
        foreach (var id in ModuleIds.All)
        {
            modules.IsEnabled(id).Should().BeTrue($"{id} must survive an absent list");
        }
    }

    // ── Rule 3: core fails open ──────────────────────────────────────────────
    [Fact]
    public void Core_is_on_even_when_the_list_omits_it()
    {
        var modules = Create("reservations", enforce: true);

        modules.IsEnforced.Should().BeTrue();
        modules.IsEnabled(ModuleIds.Core).Should().BeTrue();
        modules.EnabledModules.Should().Contain(ModuleIds.Core);
    }

    // ── Rule 4: unrecognised fails closed ────────────────────────────────────
    [Theory]
    [InlineData("kitchen-bord")]   // typo in a gate
    [InlineData("not-a-module")]
    [InlineData("")]
    public void An_unrecognised_module_id_is_off_while_enforcing(string moduleId)
    {
        var modules = Create("core,kitchen-board", enforce: true);

        modules.IsEnabled(moduleId).Should().BeFalse();
    }

    [Fact]
    public void An_unrecognised_entry_in_the_list_is_ignored_rather_than_fatal()
    {
        // provision-tenant.sh rejects these loudly at the seam; reaching here means a
        // hand-edited tenant .env, and a typo must not take a live tenant down.
        var modules = Create("core,kitchen-board,not-a-module", enforce: true);

        modules.IsEnforced.Should().BeTrue();
        modules.IsEnabled(ModuleIds.KitchenBoard).Should().BeTrue();
        modules.IsEnabled("not-a-module").Should().BeFalse();
        modules.EnabledModules.Should().BeEquivalentTo(new[] { ModuleIds.Core, ModuleIds.KitchenBoard });
    }

    // ── Rule 5: the allow-list itself ────────────────────────────────────────
    [Fact]
    public void Only_the_listed_modules_are_on_while_enforcing()
    {
        var modules = Create("core,kitchen-board,cashier,printing", enforce: true);

        modules.IsEnabled(ModuleIds.KitchenBoard).Should().BeTrue();
        modules.IsEnabled(ModuleIds.Cashier).Should().BeTrue();
        modules.IsEnabled(ModuleIds.Printing).Should().BeTrue();

        modules.IsEnabled(ModuleIds.Server).Should().BeFalse();
        modules.IsEnabled(ModuleIds.Reservations).Should().BeFalse();
        modules.IsEnabled(ModuleIds.Loyalty).Should().BeFalse();
    }

    [Theory]
    [InlineData("core, kitchen-board , cashier")]   // env-injected values carry stray whitespace
    [InlineData("CORE,Kitchen-Board,CASHIER")]      // and the registry grammar is not case-pinned
    public void The_list_is_parsed_leniently(string enabled)
    {
        var modules = Create(enabled, enforce: true);

        modules.IsEnabled(ModuleIds.KitchenBoard).Should().BeTrue();
        modules.IsEnabled(ModuleIds.Cashier).Should().BeTrue();
        modules.IsEnabled(ModuleIds.Reservations).Should().BeFalse();
    }

    [Fact]
    public void EnabledModules_is_the_whole_vocabulary_when_unrestricted()
    {
        // So a consumer can treat it as a plain allow-list without also checking the flag.
        Create("", enforce: false).EnabledModules.Should().BeEquivalentTo(ModuleIds.All);
    }

    [Fact]
    public void EnabledModules_keeps_catalog_order()
    {
        var modules = Create("printing,core,cashier", enforce: true);

        modules.EnabledModules.Should().ContainInOrder(ModuleIds.Core, ModuleIds.Cashier, ModuleIds.Printing);
    }

    [Fact]
    public void The_vocabulary_matches_the_catalog_at_the_other_end_of_the_seam()
    {
        // These exact strings also live in sofra lib/module-catalog.ts (MODULE_IDS) and
        // deploy provision-tenant.sh (KNOWN_MODULES). Drift here silently changes what a
        // tenant's registry entry means, so the list is asserted rather than assumed.
        ModuleIds.All.Should().Equal(
            "core", "kitchen-board", "cashier", "server",
            "reservations", "loyalty", "printing", "extra-languages");
    }
}
