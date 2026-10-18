using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Tenant.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Tenant;

public sealed class CustomerMobileCapabilityTests
{
    [Fact]
    public void Absent_configuration_is_inert_and_invalid_active_identity_is_rejected()
    {
        new CustomerMobileSettings().Enabled.Should().BeFalse();
        new CustomerMobileSettings { Enabled = true }.IsValid().Should().BeFalse();
        new CustomerMobileSettings
        {
            Enabled = true,
            TenantId = Guid.NewGuid(),
            Lifecycle = "active",
            EnabledCapabilities = ["staff"]
        }.IsValid().Should().BeFalse();
    }

    [Fact]
    public void Native_rollout_does_not_replace_module_entitlements_or_provider_availability()
    {
        var modules = new Mock<ITenantModules>();
        modules.Setup(value => value.IsEnabled(ModuleIds.Core)).Returns(true);
        modules.Setup(value => value.IsEnabled(ModuleIds.OnlinePayments)).Returns(true);
        var settings = new CustomerMobileSettings
        {
            Enabled = true,
            TenantId = Guid.NewGuid(),
            Lifecycle = "active",
            EnabledCapabilities = ["menu", "reservations", "online-payments"]
        };
        var capabilities = MobileCapabilityPolicy.Resolve(settings, modules.Object,
            Mock.Of<ITenantFeatures>(), true, true);
        capabilities["menu"].Should().Be("enabled");
        capabilities["reservations"].Should().Be("disabled");
        capabilities["online-payments"].Should().Be("unknown");
        capabilities["ordering"].Should().Be("disabled");
    }

    [Theory]
    [InlineData("read-only", true, true)]
    [InlineData("active", false, true)]
    [InlineData("active", true, false)]
    [InlineData("disabled", true, true)]
    public void Unknown_money_or_incompatible_lifecycle_cannot_unlock_writes(
        string lifecycle, bool hasCurrency, bool supported)
    {
        var modules = new Mock<ITenantModules>();
        modules.Setup(value => value.IsEnabled(It.IsAny<string>())).Returns(true);
        var settings = new CustomerMobileSettings { Lifecycle = lifecycle, EnabledCapabilities = ["ordering"] };
        MobileCapabilityPolicy.Resolve(settings, modules.Object, Mock.Of<ITenantFeatures>(), hasCurrency, supported)
            ["ordering"].Should().NotBe("enabled");
    }

    [Fact]
    public void Table_payments_require_separate_module_and_all_three_rollout_flags()
    {
        var modules = new Mock<ITenantModules>();
        modules.Setup(value => value.IsEnabled(It.IsAny<string>())).Returns(true);
        var features = new Mock<ITenantFeatures>();
        var settings = new CustomerMobileSettings { Lifecycle = "active", EnabledCapabilities = ["table-visits", "table-payments"] };
        MobileCapabilityPolicy.Resolve(settings, modules.Object, features.Object, true, true)
            ["table-payments"].Should().Be("disabled");
        features.SetupGet(value => value.TableGuestVisitsV1).Returns(true);
        features.SetupGet(value => value.TableAccountPaymentsV1).Returns(true);
        features.SetupGet(value => value.TableGuestAccountPaymentsV1).Returns(true);
        var resolved = MobileCapabilityPolicy.Resolve(settings, modules.Object, features.Object, true, true);
        resolved["table-visits"].Should().Be("enabled");
        resolved["table-payments"].Should().Be("unknown");
    }
}
