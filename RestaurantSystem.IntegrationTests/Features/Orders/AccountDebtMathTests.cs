using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountDebtMathTests
{
    private static readonly Guid OrderId = Guid.Parse("9bed8306-dd0e-4843-8ac5-87225365063f");
    private static readonly Guid ItemId = Guid.Parse("a3a7e0a6-7a3e-4d66-a42d-6aeb081b982d");

    [Fact]
    public void Historical_amount_is_a_contribution_and_leaves_exact_unit_balances()
    {
        var due = AccountDebtMath.CreateLine(OrderId, ItemId, 3, 10001, 5000);
        due.Should().Equal(Segment(2, 1, 1668), Segment(3, 1, 3333));
        AccountDebtMath.Total(due).Should().Be(5001);
        AccountDebtMath.Amount(due, 2000).Should().Equal(Segment(2, 1, 1668), Segment(3, 1, 332));
    }

    [Fact]
    public void Reservations_and_item_selection_use_the_remaining_minor_units()
    {
        var due = AccountDebtMath.CreateLine(OrderId, ItemId, 3, 10001, 5000);
        var available = AccountDebtMath.Subtract(due, [Segment(2, 1, 1000)]);
        available.Should().Equal(Segment(2, 1, 668), Segment(3, 1, 3333));
        AccountDebtMath.Items(available, [new(OrderId, ItemId, 2)]).Should().Equal(Segment(2, 1, 668));
        var paidUnit = () => AccountDebtMath.Items(available, [new(OrderId, ItemId, 1)]);
        paidUnit.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Extreme_quantity_has_constant_size_and_exact_last_ordinal()
    {
        var due = AccountDebtMath.CreateLine(OrderId, ItemId, int.MaxValue, int.MaxValue, 0);
        due.Should().Equal(Segment(1, int.MaxValue, 1));
        AccountDebtMath.Items(due, [new(OrderId, ItemId, int.MaxValue)])
            .Should().Equal(Segment(int.MaxValue, 1, 1));
        var lowValue = AccountDebtMath.CreateLine(OrderId, ItemId, int.MaxValue, 5, 0);
        lowValue.Should().Equal(Segment(1, 5, 1));
    }

    [Fact]
    public void Full_claim_blocks_a_second_claim_and_partial_claims_conserve_the_scope()
    {
        var due = AccountDebtMath.CreateLine(OrderId, ItemId, 2, 999, 0);
        var first = AccountDebtMath.Amount(due, 600);
        first.Should().Equal(Segment(1, 1, 500), Segment(2, 1, 100));
        var remaining = AccountDebtMath.Subtract(due, first);
        remaining.Should().Equal(Segment(2, 1, 399));
        AccountDebtMath.Total(first).Should().Be(600);
        AccountDebtMath.Total(remaining).Should().Be(399);
        AccountDebtMath.Subtract(remaining, AccountDebtMath.Amount(remaining, 399)).Should().BeEmpty();
    }

    [Fact]
    public void Unitemized_historical_debt_supports_contributions_without_fictional_items()
    {
        var due = AccountDebtMath.CreateUnitemized(OrderId, 1000, 400);
        due.Should().Equal(new AccountDebtSegment(OrderId, null, 1, 1, 600));
        AccountDebtMath.Amount(due, 200).Should().Equal(new AccountDebtSegment(OrderId, null, 1, 1, 200));
        AccountDebtMath.Subtract(due, AccountDebtMath.Amount(due, 200)).Single().TotalMinor.Should().Be(400);
    }

    [Fact]
    public void Multiple_reductions_of_one_unit_are_checked_together_without_overflow()
    {
        var due = AccountDebtMath.CreateLine(OrderId, ItemId, 1, long.MaxValue, 0);
        var excessive = () => AccountDebtMath.Subtract(due,
            [Segment(1, 1, long.MaxValue), Segment(1, 1, long.MaxValue)]);
        excessive.Should().Throw<ConflictException>();
        var overlap = () => AccountDebtMath.Amount([Segment(1, 2, 50), Segment(2, 1, 50)], 20);
        overlap.Should().Throw<BadRequestException>();
    }

    [Fact]
    public void Independent_conservation_oracle_covers_many_partial_contributions()
    {
        var random = new Random(159);
        for (var example = 0; example < 300; example++)
        {
            var total = random.Next(1, 100000);
            var paid = random.Next(total);
            var due = AccountDebtMath.CreateLine(OrderId, ItemId, random.Next(1, 1000), total, paid);
            AccountDebtMath.Total(due).Should().Be(total - paid);
            var contribution = random.Next(1, total - paid + 1);
            var claim = AccountDebtMath.Amount(due, contribution);
            AccountDebtMath.Total(claim).Should().Be(contribution);
            AccountDebtMath.Total(AccountDebtMath.Subtract(due, claim)).Should().Be(total - paid - contribution);
        }
    }

    [Fact]
    public void Scope_mismatch_overallocation_and_duplicate_units_are_refused()
    {
        var due = AccountDebtMath.CreateLine(OrderId, ItemId, 2, 100, 0);
        var excessive = () => AccountDebtMath.Subtract(due, [Segment(1, 1, 51)]);
        excessive.Should().Throw<ConflictException>();
        var outside = () => AccountDebtMath.Subtract(due, [Segment(3, 1, 1)]);
        outside.Should().Throw<ConflictException>();
        var duplicate = () => AccountDebtMath.Items(due, [new(OrderId, ItemId, 1), new(OrderId, ItemId, 1)]);
        duplicate.Should().Throw<BadRequestException>();
        var tooMuch = () => AccountDebtMath.Amount(due, 101);
        tooMuch.Should().Throw<BadRequestException>();
    }

    private static AccountDebtSegment Segment(int start, int count, long amount) =>
        new(OrderId, ItemId, start, count, amount);
}
