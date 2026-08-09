using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// S4 (SOFRA-PAYMENTS-PLAN §5). Which orders may still be handed a payment page.
///
/// <para>
/// Worth its own tests because the endpoint is ANONYMOUS: the only thing standing between a
/// scraped order id and a live Stripe page for someone else's closed order is this predicate.
/// </para>
/// </summary>
public class OnlinePaymentEligibilityTests
{
    /// <summary>
    /// The statuses an order can still be paid from. Driven off the same set the transition table
    /// allows a cancel from, which is the point: if a future lifecycle change makes one of these
    /// terminal, this test should start failing rather than the endpoint quietly staying open.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.PendingApproval)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.OutForDelivery)]
    public void A_live_order_may_be_paid(OrderStatus status)
    {
        var act = () => OnlinePaymentEligibility.EnsurePayable(Order(status, PaymentStatus.Pending));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void A_closed_order_cannot_be_paid(OrderStatus status)
    {
        var act = () => OnlinePaymentEligibility.EnsurePayable(Order(status, PaymentStatus.Pending));

        act.Should().Throw<BadRequestException>().WithMessage("*closed*");
    }

    /// <summary>
    /// Each of these means money already moved on this order. Taking more without a human deciding
    /// is worse than refusing a diner who is most likely retrying a payment that already worked.
    /// </summary>
    [Theory]
    [InlineData(PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Overpaid)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    public void An_order_that_already_took_money_cannot_be_paid_again(PaymentStatus paymentStatus)
    {
        var act = () => OnlinePaymentEligibility.EnsurePayable(Order(OrderStatus.Pending, paymentStatus));

        act.Should().Throw<BadRequestException>().WithMessage("*already been*paid*");
    }

    /// <summary>
    /// PartiallyPaid is refused, and this is the test that says so on purpose rather than by
    /// omission. The charge is <c>order.Total</c> — the WHOLE order — so admitting a part-paid
    /// order would redirect a diner who already handed over CHF 20 at the till to a page for the
    /// full CHF 50, and S5's <c>amount_total == AmountMinor</c> assertion would agree with it,
    /// because both numbers are the gross. Charging the balance instead needs that balance frozen
    /// for the 30 minutes the session is live; until then, refusing is the honest answer.
    /// </summary>
    [Fact]
    public void A_partly_paid_order_is_refused_rather_than_charged_the_gross()
    {
        var act = () => OnlinePaymentEligibility.EnsurePayable(
            Order(OrderStatus.Pending, PaymentStatus.PartiallyPaid));

        act.Should().Throw<BadRequestException>().WithMessage("*partly*");
    }

    /// <summary>
    /// The control for the two tests above: an untouched order IS payable. Without it, refusing
    /// everything would satisfy every other assertion in this file.
    /// </summary>
    [Fact]
    public void An_unpaid_order_may_be_paid()
    {
        var act = () => OnlinePaymentEligibility.EnsurePayable(
            Order(OrderStatus.Pending, PaymentStatus.Pending));

        act.Should().NotThrow();
    }

    private static Order Order(OrderStatus status, PaymentStatus paymentStatus) => new()
    {
        OrderNumber = "TEST-0001",
        Type = OrderType.Takeaway,
        Status = status,
        PaymentStatus = paymentStatus,
        Total = 40m,
        CreatedBy = nameof(OnlinePaymentEligibilityTests),
    };
}
