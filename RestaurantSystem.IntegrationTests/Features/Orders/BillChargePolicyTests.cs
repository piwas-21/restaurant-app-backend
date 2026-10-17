using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class BillChargePolicyTests
{
    [Fact]
    public void Food_paid_with_an_unpaid_fee_stays_collectible_and_blocks_settled_close()
    {
        var order = ChargedOrder();
        order.TotalPaid = 20m;
        order.Payments.Add(new OrderPayment { Amount = 20m, Status = PaymentStatus.Completed, CreatedBy = "test" });
        order.Status = OrderStatus.Completed;
        order.Total.Should().Be(20m);
        order.PayableTotal.Should().Be(25m);
        OrderSettlementEligibility.CanCollect(order).Should().BeTrue();
        OrderSettlementEligibility.CanCollectQuery().Compile()(order).Should().BeTrue();
        TableServiceSessionCloseRules.Outstanding(order).Should().Be(5m);
        TableServiceSessionCloseRules.BlockingLegacyQuery(0.01m).Compile()(order).Should().BeTrue();
        AccountDebtProjection.Project([order], new AccountMoney("CHF"), [], []).OutstandingMinor.Should().Be(500);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Fees_are_separate_from_food_and_conserved_with_captured_and_reserved_money(int version)
    {
        var order = ChargedOrder();
        var result = AccountDebtProjection.Project([order], new AccountMoney("CHF"), [],
            [new AccountDebtSegment(order.Id, null, 1, 1, 200)], billingAllocationVersion: version);
        result.OutstandingMinor.Should().Be(2500);
        result.AvailableMinor.Should().Be(2300);
        result.ReservedMinor.Should().Be(200);
        result.Outstanding.Where(segment => segment.OrderItemId is null).Sum(segment => segment.TotalMinor).Should().Be(500);
    }

    [Fact]
    public void Corrupt_materialized_charge_totals_fail_closed()
    {
        var order = ChargedOrder();
        order.BillChargeAmount = 4m;
        var act = () => AccountDebtProjection.Project([order], new AccountMoney("CHF"), [], []);
        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Rules_reject_duplicate_keys_unknown_roles_and_fractional_minor_units()
    {
        var rule = new BillChargeRuleDto("service_fee", "Service fee", true, 2m, true, ["DineIn"], ["Server"]);
        Action duplicate = () => BillChargeRules.Validate([rule, rule], "CHF");
        Action role = () => BillChargeRules.Validate([rule with { Roles = ["Customer"] }], "CHF");
        Action fractional = () => BillChargeRules.Validate([rule with { DefaultAmount = 0.001m }], "CHF");
        duplicate.Should().Throw<BadRequestException>();
        role.Should().Throw<BadRequestException>();
        fractional.Should().Throw<BadRequestException>();
        BillChargeRules.Read("[]").Should().BeEmpty();
    }

    private static Order ChargedOrder()
    {
        var id = Guid.NewGuid();
        var order = new Order
        {
            Id = id,
            Total = 20m,
            SubTotal = 20m,
            BillChargeAmount = 5m,
            Status = OrderStatus.Confirmed,
            CreatedBy = "test",
            OrderNumber = "BILL-CHARGE",
            PaymentStatus = PaymentStatus.Pending,
            Items = [new OrderItem { Id = Guid.NewGuid(), OrderId = id, ItemTotal = 20m, Quantity = 1, CreatedBy = "test" }]
        };
        order.BillChargesJson = JsonSerializer.Serialize(new[]
        {
            new OrderBillCharge(Guid.NewGuid(), "service_fee", "Service fee", 5m, "CHF", DateTime.UtcNow, "test")
        }, BillChargeRules.JsonOptions);
        return order;
    }
}
