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
    public void Existing_one_minor_unit_plan_keeps_its_original_share_positions()
    {
        var plan = AccountShareMath.Equal(235, 3, roundingIncrementMinor: 1);

        Enumerable.Range(1, 3).Select(plan.At).Should().Equal(79, 78, 78);
    }

    [Theory]
    [InlineData(460, new long[] { 150, 150, 160 })]
    [InlineData(465, new long[] { 155, 155, 155 })]
    public void Currency_increment_floors_early_shares_and_assigns_the_exact_remainder_to_the_last(
        long total, long[] expected)
    {
        var split = AccountShareMath.Equal(total, 3, 5);

        Enumerable.Range(1, 3).Select(split.At).Should().Equal(expected);
        expected.Sum().Should().Be(total);
    }

    [Fact]
    public void Currency_increment_can_produce_zero_slots_that_plan_validation_must_reject()
    {
        var split = AccountShareMath.Equal(5, 3, 5);

        Enumerable.Range(1, 3).Select(split.At).Should().Equal(0, 0, 5);
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
