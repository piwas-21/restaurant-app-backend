using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Pins the boundary between an explicitly assigned restaurant table and dine-in ordering that
/// waits for staff to accept or decline. This is pure creation policy, so a database would only
/// obscure the input that matters.
/// </summary>
public class TablelessDineInPolicyTests
{
    [Fact]
    public void Tableless_dine_in_waits_for_staff()
    {
        OnlinePaymentIntent.InitialStatus(OrderType.DineIn, tableNumber: null, paysOnline: false)
            .Should().Be(OrderStatus.Pending);
        OnlinePaymentIntent.InitialStatusNote(OrderType.DineIn, tableNumber: null, paysOnline: false)
            .Should().Be("Order created");
    }

    [Fact]
    public void Table_based_dine_in_keeps_the_existing_auto_confirm_flow()
    {
        OnlinePaymentIntent.InitialStatus(OrderType.DineIn, tableNumber: 12, paysOnline: false)
            .Should().Be(OrderStatus.Confirmed);
        OnlinePaymentIntent.InitialStatusNote(OrderType.DineIn, tableNumber: 12, paysOnline: false)
            .Should().Be("Order created and auto-confirmed (Dine-in table)");
    }

    [Theory]
    [InlineData(OrderType.DineIn, 12)]
    [InlineData(OrderType.DineIn, null)]
    [InlineData(OrderType.Takeaway, null)]
    [InlineData(OrderType.Delivery, null)]
    public void Online_payment_always_starts_pending(OrderType type, int? tableNumber)
    {
        OnlinePaymentIntent.InitialStatus(type, tableNumber, paysOnline: true)
            .Should().Be(OrderStatus.Pending);
    }
}
