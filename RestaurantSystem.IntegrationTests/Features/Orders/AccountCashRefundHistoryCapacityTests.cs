using FluentAssertions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountCashRefundHistoryCapacityTests
{
    [Fact]
    public void Quote_growth_accepts_exact_attempt_and_allocation_boundaries()
    {
        var current = AccountCashRefundHistoryCapacitySize.ForQuote(
            AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit - 1,
            AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit - 1);

        current.CanAdd(AccountCashRefundHistoryCapacityGrowth.ForQuote(1)).Should().BeTrue();
    }

    [Theory]
    [InlineData(10000, 0)]
    [InlineData(0, 10000)]
    public void Quote_growth_refuses_a_row_beyond_either_ledger_boundary(long attempts, long allocations)
    {
        var current = AccountCashRefundHistoryCapacitySize.ForQuote(attempts, allocations);

        current.CanAdd(AccountCashRefundHistoryCapacityGrowth.ForQuote(1)).Should().BeFalse();
    }

    [Fact]
    public void Resolution_growth_counts_each_cash_leg_scope_and_one_distinct_operation()
    {
        var growth = AccountCashRefundHistoryCapacityGrowth.ForResolution([2, 3]);

        growth.Attempts.Should().Be(0);
        growth.Allocations.Should().Be(0);
        growth.RefundLegs.Should().Be(2);
        growth.Intents.Should().Be(2);
        growth.AllocationReversals.Should().Be(5);
        growth.TillEvidence.Should().Be(2);
        growth.Operations.Should().Be(1);
    }

    [Fact]
    public void Multi_leg_resolution_at_projected_boundaries_fits_only_when_every_dataset_fits()
    {
        var growth = AccountCashRefundHistoryCapacityGrowth.ForResolution([2, 3]);
        var current = new AccountCashRefundHistoryCapacitySize(
            10000, 10000, 9998, 9998, 9995, 9998, 9999);

        current.CanAdd(growth).Should().BeTrue();
        (current with { AllocationReversals = 9996 }).CanAdd(growth).Should().BeFalse();
        (current with { Operations = 10000 }).CanAdd(growth).Should().BeFalse();
        (current with { Intents = 9999 }).CanAdd(growth).Should().BeFalse();
    }

    [Fact]
    public void Empty_cash_resolution_adds_no_history_rows_but_existing_overflow_still_fails_closed()
    {
        var noCashGrowth = AccountCashRefundHistoryCapacityGrowth.ForResolution([]);
        var atBoundary = new AccountCashRefundHistoryCapacitySize(10000, 10000, 10000,
            10000, 10000, 10000, 10000);

        atBoundary.CanAdd(noCashGrowth).Should().BeTrue();
        (atBoundary with { RefundLegs = 10001 }).CanAdd(noCashGrowth).Should().BeFalse();
    }

    [Fact]
    public void Invalid_counts_and_saturated_multi_scope_growth_cannot_overflow_into_acceptance()
    {
        var hugeGrowth = AccountCashRefundHistoryCapacityGrowth.ForResolution(
            [int.MaxValue, int.MaxValue]);
        var normal = new AccountCashRefundHistoryCapacitySize(0, 0, 0, 0, 0, 0, 0);

        normal.CanAdd(hugeGrowth).Should().BeFalse();
        normal.CanAdd(AccountCashRefundHistoryCapacityGrowth.ForQuote(long.MaxValue)).Should().BeFalse();
        (normal with { RefundLegs = long.MaxValue })
            .CanAdd(AccountCashRefundHistoryCapacityGrowth.ForResolution([1])).Should().BeFalse();
        (normal with { Intents = -1 })
            .CanAdd(AccountCashRefundHistoryCapacityGrowth.ForResolution([])).Should().BeFalse();
    }
}
