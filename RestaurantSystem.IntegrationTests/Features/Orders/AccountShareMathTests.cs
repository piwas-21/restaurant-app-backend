using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountShareMathTests
{
    [Fact]
    public void Equal_shares_conserve_10000_minor_units_for_three_payers()
    {
        var split = AccountShareMath.Equal(10000, 3);
        new[] { split.At(1), split.At(2), split.At(3) }.Should().Equal(3334, 3333, 3333);
    }

    [Fact]
    public void Frozen_unit_values_preserve_awkward_quantity_remainder()
    {
        var units = AccountShareMath.Equal(10001, 3);
        new[] { units.At(1), units.At(2), units.At(3) }.Should().Equal(3334, 3334, 3333);
    }

    [Fact]
    public void Large_quantity_is_a_compact_range_without_eager_expansion()
    {
        var units = AccountShareMath.Equal(5, int.MaxValue);
        units.At(1).Should().Be(1);
        units.At(5).Should().Be(1);
        units.At(6).Should().Be(0);
        units.At(int.MaxValue).Should().Be(0);
    }

    [Theory]
    [InlineData(100, new long[] { 900, 100 }, new long[] { 90, 10 })]
    [InlineData(10, new long[] { 1, 1, 1 }, new long[] { 4, 3, 3 })]
    [InlineData(5, new long[] { 0, 2, 1 }, new long[] { 0, 3, 2 })]
    [InlineData(long.MaxValue, new long[] { long.MaxValue, long.MaxValue },
        new long[] { 4611686018427387904, 4611686018427387903 })]
    public void Weighted_distribution_matches_independently_fixed_results(
        long total, long[] weights, long[] expected) =>
        AccountShareMath.Weighted(total, weights).Should().Equal(expected);

    [Fact]
    public void Invalid_scope_is_refused_before_allocation()
    {
        var noShares = () => AccountShareMath.Equal(10, 0);
        noShares.Should().Throw<BadRequestException>();
        var negative = () => AccountShareMath.Weighted(-1, [1]);
        negative.Should().Throw<BadRequestException>();
        var zeroWeights = () => AccountShareMath.Weighted(10, [0, 0]);
        zeroWeights.Should().Throw<BadRequestException>();
        var outside = () => AccountShareMath.Equal(10, 2).At(3);
        outside.Should().Throw<BadRequestException>();
    }
}
