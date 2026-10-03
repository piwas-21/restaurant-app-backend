using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class GuestAccountPaymentPolicyTests
{
    [Theory]
    [InlineData(false, true, true, true, true)]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, true, true, false)]
    public void New_payment_requires_all_visit_account_module_and_gateway_gates(
        bool visits, bool accountPayments, bool guestPayments, bool onlineModule, bool gatewayConfigured)
    {
        var policy = CreatePolicy(visits, accountPayments, guestPayments, onlineModule, gatewayConfigured);

        var action = policy.RequireNewPayment;
        action.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void New_payment_is_allowed_only_when_all_required_gates_are_on()
    {
        var policy = CreatePolicy(true, true, true, true, true);

        var action = policy.RequireNewPayment;
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Account_read_requires_existing_guest_visit_and_account_payment_features(bool visits, bool payments)
    {
        var policy = CreatePolicy(visits, payments, false, false, false);

        var action = policy.RequireAccountRead;
        action.Should().Throw<NotFoundException>();
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(99_999_999, true)]
    [InlineData(100_000_000, false)]
    public void Contribution_must_fit_the_configured_minor_unit_limits(long amount, bool allowed)
    {
        var policy = CreatePolicy(true, true, true, true, true);
        var action = () => policy.RequireContribution(amount, "CHF");
        if (allowed) action.Should().NotThrow();
        else action.Should().Throw<BadRequestException>();
        policy.ReadLimits("CHF")!.MinimumAmountMinor.Should().Be(50);
    }

    [Fact]
    public void Missing_settlement_configuration_and_conversion_require_staff_collection()
    {
        var missing = CreatePolicy(true, true, true, true, true, settlementCurrency: null);
        var missingAction = missing.RequireNewPayment;
        missingAction.Should().Throw<NotFoundException>();
        var configured = CreatePolicy(true, true, true, true, true);
        configured.ReadLimits("EUR").Should().BeNull();
        var convertedContribution = () => configured.RequireContribution(1000, "EUR");
        convertedContribution.Should().Throw<NotFoundException>();
    }

    [Theory]
    [InlineData("CHF", 49, false)]
    [InlineData("CHF", 50, true)]
    [InlineData("EUR", 49, false)]
    [InlineData("USD", 50, true)]
    [InlineData("GBP", 29, false)]
    [InlineData("GBP", 30, true)]
    [InlineData("AED", 199, false)]
    [InlineData("AED", 200, true)]
    [InlineData("TRY", 1000, false)]
    public void Provider_floor_or_unsupported_currency_disables_new_collection(
        string currency, long minimum, bool enabled)
    {
        var policy = CreatePolicy(true, true, true, true, true, currency, minimum);
        var start = policy.RequireNewPayment;
        if (enabled)
        {
            start.Should().NotThrow();
            policy.ReadLimits(currency)!.MinimumAmountMinor.Should().Be(minimum);
        }
        else
        {
            start.Should().Throw<NotFoundException>();
            policy.ReadLimits(currency).Should().BeNull();
        }
    }

    private static GuestAccountPaymentPolicy CreatePolicy(
        bool visits, bool accountPayments, bool guestPayments, bool onlineModule, bool gatewayConfigured,
        string? settlementCurrency = "CHF", long minimum = 50)
    {
        var features = new TenantFeatures(Options.Create(new TenantFeatureSettings
        {
            TableGuestVisitsV1 = visits,
            TableAccountPaymentsV1 = accountPayments,
            TableGuestAccountPaymentsV1 = guestPayments
        }));
        var modules = Mock.Of<ITenantModules>(value => value.IsEnabled(ModuleIds.OnlinePayments) == onlineModule);
        var gateway = Mock.Of<IStripeGateway>(value => value.IsConfigured == gatewayConfigured);
        return new GuestAccountPaymentPolicy(features, modules, gateway,
            Options.Create(new AccountOnlineContributionSettings
            {
                SettlementCurrency = settlementCurrency,
                MinimumAmountMinor = minimum
            }));
    }
}
