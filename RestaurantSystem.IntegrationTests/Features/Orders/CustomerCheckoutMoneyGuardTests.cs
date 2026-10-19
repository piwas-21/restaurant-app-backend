using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CustomerCheckoutMoneyGuardTests
{
    private static Order Order() => new() { CreatedBy = "quote-money" };

    [Fact]
    public void Legitimate_zero_prices_and_negative_component_modifiers_remain_representable()
    {
        var order = Order();
        order.CustomerDiscountAmount = 5.196m;
        order.Items = [new()
        {
            UnitPrice = -2m, ItemTotal = 0m, ParentOrderItemId = Guid.NewGuid(), CreatedBy = "quote-money"
        }];
        CustomerCheckoutMoneyGuard.Ensure(order);
    }

    [Fact]
    public void Database_precision_range_and_root_price_rules_are_checked_independently_of_total()
    {
        var invalid = new List<Order>();
        var subtotal = Order(); subtotal.SubTotal = 100_000_000m; invalid.Add(subtotal);
        var total = Order(); total.Total = 0.001m; invalid.Add(total);
        var fee = Order(); fee.DeliveryFee = -1m; invalid.Add(fee);
        var line = Order(); line.Items = [new() { UnitPrice = -2m, CreatedBy = "quote-money" }]; invalid.Add(line);
        foreach (var order in invalid)
        {
            var act = () => CustomerCheckoutMoneyGuard.Ensure(order);
            act.Should().Throw<BadRequestException>();
        }
    }
}
