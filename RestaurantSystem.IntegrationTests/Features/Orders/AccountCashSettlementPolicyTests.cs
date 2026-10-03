using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountCashSettlementPolicyTests
{
    [Fact]
    public void Swiss_cash_quote_freezes_round_up_terms_in_minor_units()
    {
        var quote = AccountCashSettlementPolicy.Resolve("chf", PaymentMethod.Cash, 333);

        quote.PolicyVersion.Should().Be("chf-cash-5-rappen-v1");
        quote.Currency.Should().Be("CHF");
        quote.PaymentMethod.Should().Be(PaymentMethod.Cash);
        quote.ExactAmountMinor.Should().Be(333);
        quote.AdjustmentMinor.Should().Be(2);
        quote.DueAmountMinor.Should().Be(335);
    }

    [Theory]
    [InlineData(330, 0, 330)]
    [InlineData(331, -1, 330)]
    [InlineData(332, -2, 330)]
    [InlineData(333, 2, 335)]
    [InlineData(334, 1, 335)]
    [InlineData(335, 0, 335)]
    public void Swiss_cash_rounding_conserves_exact_value_and_auditable_adjustment(
        long exact, long adjustment, long due)
    {
        var quote = AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, exact);

        quote.AdjustmentMinor.Should().Be(adjustment);
        quote.DueAmountMinor.Should().Be(due);
        checked(quote.ExactAmountMinor + quote.AdjustmentMinor).Should().Be(quote.DueAmountMinor);
        (quote.DueAmountMinor % 5).Should().Be(0);
    }

    [Theory]
    [InlineData("CHF", PaymentMethod.CreditCard)]
    [InlineData("CHF", PaymentMethod.OnlinePayment)]
    [InlineData("CHF", PaymentMethod.DebitCard)]
    [InlineData("EUR", PaymentMethod.Cash)]
    [InlineData("EUR", PaymentMethod.CreditCard)]
    [InlineData("GBP", PaymentMethod.OnlinePayment)]
    [InlineData("JPY", PaymentMethod.Cash)]
    public void Non_cash_or_non_chf_quotes_remain_exact(string currency, PaymentMethod method)
    {
        var quote = AccountCashSettlementPolicy.Resolve(currency, method, 333);

        quote.PolicyVersion.Should().Be("exact-v1");
        quote.Currency.Should().Be(currency);
        quote.PaymentMethod.Should().Be(method);
        quote.ExactAmountMinor.Should().Be(333);
        quote.AdjustmentMinor.Should().Be(0);
        quote.DueAmountMinor.Should().Be(333);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Cash_amount_that_would_round_to_zero_is_refused(long exact)
    {
        var resolve = () => AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, exact);

        resolve.Should().Throw<BadRequestException>().WithMessage("*rounds to zero*");
    }

    [Fact]
    public void Invalid_currency_amount_and_account_method_are_refused()
    {
        var currency = () => AccountCashSettlementPolicy.Resolve("", PaymentMethod.Cash, 100);
        var amount = () => AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, 0);
        var method = () => AccountCashSettlementPolicy.Resolve("CHF", (PaymentMethod)999, 100);

        currency.Should().Throw<BadRequestException>();
        amount.Should().Throw<BadRequestException>();
        method.Should().Throw<BadRequestException>();
    }

    [Fact]
    public void Partial_then_full_refund_telescopes_to_original_retained_cash_due()
    {
        var original = AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, 333);

        var first = AccountCashRefundPolicy.Project(original, 0, 0, 101);
        first.ExactRefundAmountMinor.Should().Be(101);
        first.CashRefundAmountMinor.Should().Be(105);
        first.RefundAdjustmentMinor.Should().Be(4);
        first.RetainedExactAmountMinor.Should().Be(232);
        first.RetainedCashDueMinor.Should().Be(230);

        var final = AccountCashRefundPolicy.Project(original,
            first.ExactRefundAmountMinor, first.CashRefundAmountMinor, 232);
        final.CashRefundAmountMinor.Should().Be(230);
        final.RefundAdjustmentMinor.Should().Be(-2);
        final.RetainedExactAmountMinor.Should().Be(0);
        final.RetainedCashDueMinor.Should().Be(0);
        (first.CashRefundAmountMinor + final.CashRefundAmountMinor).Should().Be(original.DueAmountMinor);
        (first.RefundAdjustmentMinor + final.RefundAdjustmentMinor).Should().Be(original.AdjustmentMinor);
    }

    [Fact]
    public void Refund_partitions_round_retained_balance_and_can_quote_zero_physical_change()
    {
        var original = AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, 333);
        var exactRefunded = 0L;
        var cashRefunded = 0L;
        var adjustmentTotal = 0L;

        foreach (var exactDelta in new long[] { 1, 2, 100, 230 })
        {
            var refund = AccountCashRefundPolicy.Project(original, exactRefunded, cashRefunded, exactDelta);
            (refund.CashRefundAmountMinor % 5).Should().Be(0);
            exactRefunded += exactDelta;
            cashRefunded += refund.CashRefundAmountMinor;
            adjustmentTotal += refund.RefundAdjustmentMinor;
            if (exactDelta == 2) refund.CashRefundAmountMinor.Should().Be(0);
        }

        exactRefunded.Should().Be(original.ExactAmountMinor);
        cashRefunded.Should().Be(original.DueAmountMinor);
        adjustmentTotal.Should().Be(original.AdjustmentMinor);
    }

    [Fact]
    public void Negative_original_adjustment_is_preserved_across_partial_and_full_refunds()
    {
        var original = AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, 331);
        original.AdjustmentMinor.Should().Be(-1);

        var partial = AccountCashRefundPolicy.Project(original, 0, 0, 31);
        partial.CashRefundAmountMinor.Should().Be(30);
        partial.RefundAdjustmentMinor.Should().Be(-1);

        var final = AccountCashRefundPolicy.Project(original, 31, 30, 300);
        final.CashRefundAmountMinor.Should().Be(300);
        final.RetainedCashDueMinor.Should().Be(0);
        (partial.CashRefundAmountMinor + final.CashRefundAmountMinor).Should().Be(original.DueAmountMinor);
        (partial.RefundAdjustmentMinor + final.RefundAdjustmentMinor).Should().Be(original.AdjustmentMinor);
    }

    [Fact]
    public void One_minor_refund_partitions_conserve_every_supported_chf_cash_quote()
    {
        for (var exactMinor = 3; exactMinor <= 1000; exactMinor++)
        {
            var original = AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, exactMinor);
            Math.Abs(original.AdjustmentMinor).Should().BeLessThanOrEqualTo(2);
            long exactRefunded = 0;
            long physicalRefunded = 0;
            long adjustmentRefunded = 0;

            while (exactRefunded < original.ExactAmountMinor)
            {
                var refund = AccountCashRefundPolicy.Project(original, exactRefunded, physicalRefunded, 1);
                (refund.CashRefundAmountMinor % AccountCashSettlementPolicy.SwissCashIncrementMinor)
                    .Should().Be(0);
                exactRefunded++;
                physicalRefunded += refund.CashRefundAmountMinor;
                adjustmentRefunded += refund.RefundAdjustmentMinor;
            }

            exactRefunded.Should().Be(original.ExactAmountMinor);
            physicalRefunded.Should().Be(original.DueAmountMinor);
            adjustmentRefunded.Should().Be(original.AdjustmentMinor);
        }
    }

    [Fact]
    public void Exact_policy_refunds_exact_minor_amount_and_inconsistent_prior_evidence_is_refused()
    {
        var exact = AccountCashSettlementPolicy.Resolve("EUR", PaymentMethod.Cash, 333);
        var refund = AccountCashRefundPolicy.Project(exact, 0, 0, 101);
        refund.CashRefundAmountMinor.Should().Be(101);
        refund.RefundAdjustmentMinor.Should().Be(0);

        var exactCard = AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.CreditCard, 333);
        var cardRefund = () => AccountCashRefundPolicy.Project(exactCard, 0, 0, 101);
        var aboveOriginalRetained = () => AccountCashSettlementPolicy.RetainedDue(exact, 334);
        cardRefund.Should().Throw<BadRequestException>();
        aboveOriginalRetained.Should().Throw<ConflictException>().WithMessage("*terms*");

        var original = AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, 333);
        var inconsistent = () => AccountCashRefundPolicy.Project(original, 101, 104, 1);
        var overshoot = () => AccountCashRefundPolicy.Project(original, 101, 105, 233);
        var changedPolicy = () => AccountCashRefundPolicy.Project(
            original with { PolicyVersion = "future-rounding-v2" }, 0, 0, 1);
        var changedTerms = () => AccountCashRefundPolicy.Project(
            original with { AdjustmentMinor = 1 }, 0, 0, 1);

        inconsistent.Should().Throw<ConflictException>().WithMessage("*reconcile*");
        overshoot.Should().Throw<BadRequestException>();
        changedPolicy.Should().Throw<ConflictException>().WithMessage("*policy version*");
        changedTerms.Should().Throw<ConflictException>().WithMessage("*terms*");
    }

    [Theory]
    [InlineData(332, 3, 335)]
    [InlineData(333, -3, 330)]
    public void Impossible_chf_rounding_terms_are_rejected_even_when_due_is_a_five_rappen_multiple(
        long exactMinor, long adjustmentMinor, long dueMinor)
    {
        var impossible = new CashSettlementQuote(
            AccountCashSettlementPolicy.SwissCashFiveRappenV1, "CHF", PaymentMethod.Cash,
            exactMinor, adjustmentMinor, dueMinor);

        var captureTerms = () => AccountCashSettlementPolicy.RequireMatches(
            impossible, "CHF", PaymentMethod.Cash, exactMinor);
        var refundTerms = () => AccountCashSettlementPolicy.RetainedDue(impossible, exactMinor);

        captureTerms.Should().Throw<ConflictException>();
        refundTerms.Should().Throw<ConflictException>();
    }
}
