using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountMoneyTests
{
    [Theory]
    [InlineData("CHF")]
    [InlineData("eur")]
    [InlineData("GBP")]
    [InlineData("USD")]
    [InlineData("AED")]
    public void Currency_rail_roundtrips_exact_minor_units(string currency)
    {
        var money = new AccountMoney(currency);
        money.ToMinor(100.01m).Should().Be(10001);
        money.ToMajor(3334).Should().Be(33.34m);
        money.ToMinor(0).Should().Be(0);
    }

    [Fact]
    public void Unsupported_currency_and_fractional_minor_units_are_refused()
    {
        var unsupported = () => new AccountMoney("JPY");
        unsupported.Should().Throw<BadRequestException>();
        var fractional = () => new AccountMoney("CHF").ToMinor(0.001m);
        fractional.Should().Throw<BadRequestException>();
    }
}
