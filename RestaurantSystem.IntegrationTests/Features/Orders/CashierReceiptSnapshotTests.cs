using FluentAssertions;
using RestaurantSystem.Api.Features.CashierPrinting.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public class CashierReceiptSnapshotTests
{
    [Fact]
    public void Full_bill_preserves_all_rounds_money_refunds_and_credit_without_collectible_refund_debt()
    {
        var refunded = new OrderDto
        {
            Status = "Completed",
            Total = 10,
            TotalPaid = 0,
            Items = [new() { ProductName = "Tea", Quantity = 1, SpecialInstructions = "private comment" }]
        };
        var paid = new OrderDto
        {
            Status = "Completed",
            Total = 20,
            TotalPaid = 22,
            Items = [new() { ProductName = "Kofte", Quantity = 1 }]
        };
        var bill = new TableBillDto
        {
            ServiceSessionId = Guid.NewGuid(),
            Total = 30,
            TotalPaid = 22,
            Remaining = 10,
            Credit = 2,
            Orders = [refunded, paid],
            Rounds = [
                new() { Order = refunded, SettlementState = "Refunded", Outstanding = 10, RefundedAmount = 10 },
                new() { Order = paid, SettlementState = "Overpaid", Outstanding = 0, Credit = 2 }]
        };
        var receipt = CashierReceiptSnapshot.FromBill(bill);
        receipt.Items.Should().HaveCount(2);
        receipt.Total.Should().Be(30);
        receipt.TotalPaid.Should().Be(22);
        receipt.RemainingAmount.Should().Be(0);
        receipt.CashierBillRefundedAmount.Should().Be(10);
        receipt.CashierBillCredit.Should().Be(2);
        receipt.PaymentStatus.Should().Be("PartiallyRefunded");
        receipt.Items[0].SpecialInstructions.Should().BeNull();
        refunded.Items[0].SpecialInstructions.Should().Be("private comment", "snapshot redaction must not mutate the staff DTO");
    }
}
