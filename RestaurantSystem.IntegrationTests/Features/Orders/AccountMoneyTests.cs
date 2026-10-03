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

    [Fact]
    public void Largest_minor_unit_value_is_exact_and_one_cent_beyond_is_refused()
    {
        var money = new AccountMoney("CHF");
        money.ToMinor(92233720368547758.07m).Should().Be(long.MaxValue);
        money.ToMajor(long.MaxValue).Should().Be(92233720368547758.07m);
        var outside = () => money.ToMinor(92233720368547758.08m);
        outside.Should().Throw<BadRequestException>();
    }

    [Fact]
    public void Negative_amount_is_refused_in_both_conversion_directions()
    {
        var money = new AccountMoney("CHF");
        var negativeMajor = () => money.ToMinor(-0.01m);
        var negativeMinor = () => money.ToMajor(-1);
        negativeMajor.Should().Throw<BadRequestException>();
        negativeMinor.Should().Throw<BadRequestException>();
    }
}
