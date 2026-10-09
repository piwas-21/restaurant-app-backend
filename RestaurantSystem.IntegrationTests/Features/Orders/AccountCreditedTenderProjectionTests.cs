using FluentAssertions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountCreditedTenderProjectionTests
{
    [Theory]
    [InlineData(5, 2000)]
    [InlineData(15, 1000)]
    [InlineData(25, 0)]
    public void Native_tender_after_first_unit_credit_pays_retained_food_then_tip_and_fee(
        decimal nativeAmount, long expectedOutstanding)
    {
        var source = Source(nativeAmount);
        var account = AccountDebtProjection.Project([source], new("CHF"), [], [], [FirstUnitVoid(source)]);

        account.OutstandingMinor.Should().Be(expectedOutstanding);
        account.AvailableMinor.Should().Be(expectedOutstanding);
        account.Outstanding.Should().NotContain(segment => segment.OrderItemId == source.Items.Single().Id
            && segment.StartOrdinal == 1);
    }

    [Fact]
    public void Frozen_item_capture_keeps_ownership_while_native_tender_pays_other_retained_units()
    {
        var source = Source(20m);
        var itemId = source.Items.Single().Id;
        var captured = new[] { new AccountDebtSegment(source.Id, itemId, 3, 1, 1000) };

        var account = AccountDebtProjection.Project([source], new("CHF"), captured, [], [FirstUnitVoid(source)]);

        account.Outstanding.Should().Equal(new AccountDebtSegment(source.Id, null, 1, 1, 500));
        account.OutstandingMinor.Should().Be(500);
        captured.Should().Equal(new AccountDebtSegment(source.Id, itemId, 3, 1, 1000));
    }

    private static Order Source(decimal paid)
    {
        var orderId = Guid.NewGuid();
        return new Order
        {
            Id = orderId,
            Type = OrderType.DineIn,
            Status = OrderStatus.Confirmed,
            OrderDate = DateTime.UnixEpoch,
            Total = 35m,
            Tip = 2m,
            DeliveryFee = 3m,
            BillingCreditAmount = 10m,
            TotalPaid = paid,
            CreatedBy = nameof(AccountCreditedTenderProjectionTests),
            Items = [new OrderItem { Id = Guid.NewGuid(), OrderId = orderId,
                Quantity = 3, UnitPrice = 10m, ItemTotal = 30m,
                CreatedBy = nameof(AccountCreditedTenderProjectionTests) }],
            Payments = [new OrderPayment { Id = Guid.NewGuid(), OrderId = orderId,
                Amount = paid, Currency = "CHF", PaymentMethod = PaymentMethod.Cash,
                Status = PaymentStatus.Completed, CreatedBy = nameof(AccountCreditedTenderProjectionTests) }]
        };
    }

    private static OrderAmendment FirstUnitVoid(Order source) => new()
    {
        Id = Guid.NewGuid(),
        SourceOrderId = source.Id,
        State = OrderAmendmentState.Committed,
        ChangesJson = OrderAmendmentJson.Serialize(new[]
        {
            new OrderAmendmentChangeSnapshot(source.Items.Single().Id, OrderAmendmentChangeKind.Void,
                1, 1, false, new OrderItemDto { Id = source.Items.Single().Id, Quantity = 1 }, null)
        }),
        CreatedBy = nameof(AccountCreditedTenderProjectionTests)
    };
}
