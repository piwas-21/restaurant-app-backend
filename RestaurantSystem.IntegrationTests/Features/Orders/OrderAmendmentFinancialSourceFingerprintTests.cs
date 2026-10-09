using FluentAssertions;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderAmendmentFinancialSourceFingerprintTests
{
    [Fact]
    public void Kitchen_and_round_versions_do_not_change_the_frozen_financial_fingerprint()
    {
        var (source, amendment) = NewSource();
        var original = Fingerprint(source, amendment);

        source.Version++;
        source.Status = OrderStatus.Preparing;
        source.IsKitchenReleased = true;
        source.ServiceSession!.AccountRevision++;
        source.ServiceSession.Version++;

        Fingerprint(source, amendment).Should().Be(original,
            "fulfillment and sibling-round changes do not alter captured refund ownership");
    }

    [Fact]
    public void Tender_and_order_charge_mutations_change_the_frozen_financial_fingerprint()
    {
        var (source, amendment) = NewSource();
        var original = Fingerprint(source, amendment);

        source.TotalPaid += 0.01m;
        Fingerprint(source, amendment).Should().NotBe(original);
        source.TotalPaid -= 0.01m;
        source.Payments.Single().RefundedAmount = 0.01m;
        Fingerprint(source, amendment).Should().NotBe(original);
    }

    private static string Fingerprint(Order source, OrderAmendment amendment) =>
        OrderAmendmentFinancialSourceFingerprint.Create(new OrderAmendmentFinancialSourceState(source, amendment, [amendment], [], [], [],
            new Dictionary<Guid, long>(), [], [], "CHF"));

    private static (Order Source, OrderAmendment Amendment) NewSource()
    {
        var orderId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var amendment = new OrderAmendment
        {
            Id = Guid.NewGuid(),
            SourceOrderId = orderId,
            ServiceSessionId = sessionId,
            ActorUserId = Guid.NewGuid(),
            ActorRole = UserRole.Admin.ToString(),
            State = OrderAmendmentState.Committed,
            ChangesJson = "[]",
            FinancialResolutionJson = "pending snapshot",
            SourceSnapshotJson = "frozen source",
            CreatedBy = nameof(OrderAmendmentFinancialSourceFingerprintTests)
        };
        var source = new Order
        {
            Id = orderId,
            Type = OrderType.DineIn,
            Status = OrderStatus.Confirmed,
            Version = 4,
            SubTotal = 20m,
            Total = 20m,
            TotalPaid = 20m,
            RemainingAmount = 0m,
            PaymentStatus = PaymentStatus.Completed,
            ServiceSessionId = sessionId,
            ServiceSession = new TableServiceSession
            {
                Id = sessionId,
                Status = TableServiceSessionStatus.Open,
                AccountRevision = 3,
                Version = 2,
                BillingAllocationVersion = 1,
                Currency = "CHF",
                CreatedBy = nameof(OrderAmendmentFinancialSourceFingerprintTests)
            },
            Items = [new OrderItem
            {
                Id = itemId,
                OrderId = orderId,
                Quantity = 2,
                UnitPrice = 10m,
                ItemTotal = 20m,
                CreatedBy = nameof(OrderAmendmentFinancialSourceFingerprintTests)
            }],
            Payments = [new OrderPayment
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 20m,
                Currency = "CHF",
                Status = PaymentStatus.Completed,
                CreatedBy = nameof(OrderAmendmentFinancialSourceFingerprintTests)
            }],
            CreatedBy = nameof(OrderAmendmentFinancialSourceFingerprintTests)
        };
        amendment.ServiceSessionId = source.ServiceSessionId;
        return (source, amendment);
    }
}
