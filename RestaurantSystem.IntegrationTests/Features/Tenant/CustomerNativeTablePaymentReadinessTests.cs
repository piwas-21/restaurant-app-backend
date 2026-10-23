using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Tenant.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Tenant;

public sealed class CustomerNativeTablePaymentReadinessTests
{
    [Theory]
    [InlineData("CHF")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("USD")]
    [InlineData("AED")]
    public void Requires_explicit_native_approval_and_all_configured_hosted_authority(string currency)
    {
        var settings = Configured();
        var gateway = new Mock<IStripeGateway>(MockBehavior.Strict);
        gateway.SetupGet(value => value.IsConfigured).Returns(true);
        var readiness = new CustomerNativeTablePaymentReadiness(Options.Create(settings), Policy(gateway.Object, currency));
        readiness.IsReady(currency).Should().BeTrue();
        settings.TablePaymentsEnabled = false;
        readiness.IsReady(currency).Should().BeFalse();
        gateway.VerifyGet(value => value.IsConfigured, Times.Once());
        gateway.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("hosted")]
    [InlineData("origin")]
    [InlineData("slug")]
    [InlineData("pages")]
    [InlineData("budget")]
    [InlineData("provider")]
    [InlineData("currency")]
    [InlineData("scale")]
    [InlineData("case")]
    [InlineData("spacing")]
    [InlineData("settlement")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    [InlineData("guest")]
    [InlineData("module")]
    public void Withholds_readiness_for_unproved_configuration_without_provider_IO(string invalid)
    {
        var settings = Configured();
        var currency = "CHF";
        if (invalid == "hosted") settings.Enabled = false;
        if (invalid == "origin") settings.LinkOrigin = "http://links.example.test";
        if (invalid == "slug") settings.TenantSlug = string.Empty;
        if (invalid == "pages") settings.HostedPageOrigins = [];
        if (invalid == "budget") settings.ReconciliationBudgetSeconds = 0;
        if (invalid == "currency") currency = string.Empty;
        if (invalid == "scale") currency = "JPY";
        if (invalid == "case") currency = "chf";
        if (invalid == "spacing") currency = "CHF ";
        var gateway = new Mock<IStripeGateway>(MockBehavior.Strict);
        gateway.SetupGet(value => value.IsConfigured).Returns(invalid != "provider");
        var readiness = new CustomerNativeTablePaymentReadiness(Options.Create(settings), Policy(gateway.Object, "CHF", invalid));
        readiness.IsReady(currency).Should().BeFalse();
        gateway.VerifyGet(value => value.IsConfigured, Times.AtMostOnce());
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public void Default_settings_never_opt_in_a_tenant()
    {
        var settings = new CustomerHostedCheckoutSettings();
        settings.TablePaymentsEnabled.Should().BeFalse();
        new CustomerNativeTablePaymentReadiness(Options.Create(settings), Policy(Mock.Of<IStripeGateway>(), "CHF"))
            .IsReady("CHF").Should().BeFalse();
    }

    private static GuestAccountPaymentPolicy Policy(IStripeGateway gateway, string currency, string? invalid = null)
    {
        var features = new Mock<ITenantFeatures>();
        features.SetupGet(value => value.TableGuestVisitsV1).Returns(true);
        features.SetupGet(value => value.TableAccountPaymentsV1).Returns(true);
        features.SetupGet(value => value.TableGuestAccountPaymentsV1).Returns(invalid != "guest");
        var modules = new Mock<ITenantModules>();
        modules.Setup(value => value.IsEnabled(It.IsAny<string>())).Returns(invalid != "module");
        var limits = new AccountOnlineContributionSettings
        {
            SettlementCurrency = invalid == "settlement" ? "EUR" : currency,
            MinimumAmountMinor = invalid == "minimum" ? 1 : 200,
            MaximumAmountMinor = invalid == "maximum" ? 1 : AccountOnlineContributionSettings.ProviderMaximumAmountMinor
        };
        return new GuestAccountPaymentPolicy(features.Object, modules.Object, gateway, Options.Create(limits));
    }

    private static CustomerHostedCheckoutSettings Configured() => new()
    {
        Enabled = true,
        TablePaymentsEnabled = true,
        LinkOrigin = "https://links.example.test",
        TenantSlug = "fixture",
        HostedPageOrigins = ["https://checkout.example.test"]
    };
}
