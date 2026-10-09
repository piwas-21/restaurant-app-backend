using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class FrozenOrderChargeMathTests
{
    private static readonly Guid OrderId = Guid.Parse("91000000-0000-0000-0000-000000000001");
    private static readonly Guid First = Guid.Parse("91000000-0000-0000-0000-000000000002");
    private static readonly Guid Second = Guid.Parse("91000000-0000-0000-0000-000000000003");

    [Fact]
    public void Shuffled_food_lines_keep_the_same_remainder_and_separate_charges()
    {
        var order = Source(15.01m, 2m, 3m,
            new OrderItem { Id = Second, OrderId = OrderId, Quantity = 1, ItemTotal = 5m, CreatedBy = "test" },
            new OrderItem { Id = First, OrderId = OrderId, Quantity = 1, ItemTotal = 5m, CreatedBy = "test" });
        var first = FrozenOrderChargeMath.Read(order, new("CHF"));
        order.Items = order.Items.Reverse().ToList();
        var shuffled = FrozenOrderChargeMath.Read(order, new("CHF"));

        first.FoodMinor.Should().Be(1001);
        first.TipMinor.Should().Be(200);
        first.FeeMinor.Should().Be(300);
        first.FoodLines.Select(line => (line.Item.Id, line.AmountMinor)).Should().Equal((First, 501L), (Second, 500L));
        shuffled.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData(12, 2, 0, 200)]
    [InlineData(15, 2, 3, 500)]
    public async Task Food_void_quote_and_account_both_retain_tip_and_fee(
        int total, int tip, int fee, long retainedMinor)
    {
        var order = Source(total, tip, fee,
            new OrderItem { Id = First, OrderId = OrderId, Quantity = 1, ItemTotal = 10m, CreatedBy = "test" });
        var change = new OrderAmendmentChangeSnapshot(First, OrderAmendmentChangeKind.Void,
            1, 1, false, new OrderItemDto { Id = First, Quantity = 1 }, null);
        var resolver = new Mock<IOrderDisplayCurrencyResolver>();
        resolver.Setup(value => value.Resolve(order)).Returns("CHF");
        var financial = new OrderAmendmentFinancialResolutionService(resolver.Object, Mock.Of<IOrderBillingAdjustmentWriter>());
        var preview = await financial.PreviewAsync(order, [change], null, CancellationToken.None);
        var amendment = new OrderAmendment
        {
            Id = Guid.NewGuid(),
            SourceOrderId = OrderId,
            State = OrderAmendmentState.Committed,
            ChangesJson = OrderAmendmentJson.Serialize(new[] { change }),
            CreatedBy = "test"
        };
        await financial.StageAsync(amendment, order, preview, CancellationToken.None);

        // Arrange the separately materialized credit; production persistence is checked by PG oracles.
        order.BillingCreditAmount = 10m;
        var account = AccountDebtProjection.Project([order], new("CHF"), [], [], [amendment]);

        preview.RemovedUnitValueMinor.Should().Be(1000);
        account.OutstandingMinor.Should().Be(retainedMinor);
        account.Outstanding.Should().Equal(new AccountDebtSegment(OrderId, null, 1, 1, retainedMinor));
        order.Total.Should().Be(total, "the original price remains history");
    }

    [Fact]
    public void Discounted_fee_cannot_create_debt_beyond_the_original_charge()
    {
        var order = Source(3m, 2m, 4m,
            new OrderItem { Id = First, OrderId = OrderId, Quantity = 1, ItemTotal = 10m, CreatedBy = "test" });
        var frozen = FrozenOrderChargeMath.Read(order, new("CHF"));
        frozen.FoodMinor.Should().Be(0);
        frozen.TipMinor.Should().Be(200);
        frozen.FeeMinor.Should().Be(100);
        frozen.FoodLines.Single().AmountMinor.Should().Be(0);
    }

    private static Order Source(decimal total, decimal tip, decimal fee, params OrderItem[] items) => new()
    {
        Id = OrderId,
        Total = total,
        Tip = tip,
        DeliveryFee = fee,
        Type = OrderType.DineIn,
        Status = OrderStatus.Confirmed,
        OrderDate = DateTime.UnixEpoch,
        Items = items.ToList(),
        CreatedBy = "test"
    };
}
