using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountBillingAllocationCompatibilityTests
{
    private static readonly Guid OrderId = Guid.Parse("92000000-0000-0000-0000-000000000001");
    private static readonly Guid ItemId = Guid.Parse("92000000-0000-0000-0000-000000000002");

    [Fact]
    public void Legacy_full_total_capture_remains_readable_without_remapping_ownership()
    {
        var order = Source();
        order.TotalPaid = 12m;
        order.Payments.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            Amount = 12m,
            Status = PaymentStatus.Completed,
            CreatedBy = "test"
        });
        var captured = new[] { new AccountDebtSegment(OrderId, ItemId, 1, 1, 1200) };

        var account = AccountDebtProjection.Project([order], new("CHF"), captured, [],
            billingAllocationVersion: 0);

        account.OutstandingMinor.Should().Be(0);
        account.AvailableMinor.Should().Be(0);
        captured.Single().Should().Be(new AccountDebtSegment(OrderId, ItemId, 1, 1, 1200));
    }

    [Fact]
    public void Legacy_in_flight_reservation_keeps_its_exact_original_scope()
    {
        var reserved = new[] { new AccountDebtSegment(OrderId, ItemId, 1, 1, 1200) };
        var account = AccountDebtProjection.Project([Source()], new("CHF"), [], reserved,
            billingAllocationVersion: 0);
        account.OutstandingMinor.Should().Be(1200);
        account.ReservedMinor.Should().Be(1200);
        account.AvailableMinor.Should().Be(0);
    }

    [Fact]
    public void Legacy_food_void_with_tip_requires_explicit_reconciliation()
    {
        var change = new OrderAmendmentChangeSnapshot(ItemId, OrderAmendmentChangeKind.Void, 1, 1,
            false, new OrderItemDto { Id = ItemId, Quantity = 1 }, null);
        var amendment = new OrderAmendment
        {
            Id = Guid.NewGuid(),
            SourceOrderId = OrderId,
            State = OrderAmendmentState.Committed,
            ChangesJson = OrderAmendmentJson.Serialize(new[] { change }),
            CreatedBy = "test"
        };
        var read = () => AccountDebtProjection.Project([Source()], new("CHF"), [], [], [amendment],
            billingAllocationVersion: 0);
        read.Should().Throw<ConflictException>().WithMessage("*financial reconciliation*");
    }

    [Theory]
    [InlineData(OrderAmendmentChangeKind.Void)]
    [InlineData(OrderAmendmentChangeKind.Replace)]
    public void Legacy_food_removal_is_rejected_by_the_shared_quote_and_commit_policy(
        OrderAmendmentChangeKind kind)
    {
        var order = Source();
        order.ServiceSession = new TableServiceSession
        {
            BillingAllocationVersion = 0,
            Status = TableServiceSessionStatus.Open,
            AccountRevision = 3,
            CreatedBy = "test"
        };
        var request = new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = order.Version,
            ExpectedAccountRevision = 3,
            Changes = [new OrderAmendmentLineChangeRequest
                { OrderItemId = ItemId, Kind = kind, StartOrdinal = 1, Quantity = 1 }]
        };
        var quoteOrCommit = () => OrderAmendmentPolicy.ValidateOrderContext(
            order, request, NoRefundAuthority());
        quoteOrCommit.Should().Throw<ConflictException>().WithMessage("*financial reconciliation*");
    }

    [Fact]
    public void Unknown_scope_version_never_guesses_a_payment_projection()
    {
        var read = () => AccountDebtProjection.Project([Source()], new("CHF"), [], [],
            billingAllocationVersion: 2);
        read.Should().Throw<ConflictException>().WithMessage("*unsupported charge allocation model*");
    }

    private static Order Source() => new()
    {
        Id = OrderId,
        Total = 12m,
        Tip = 2m,
        Type = OrderType.DineIn,
        Status = OrderStatus.Confirmed,
        OrderDate = DateTime.UnixEpoch,
        CreatedBy = "test",
        Items = [new OrderItem { Id = ItemId, OrderId = OrderId, Quantity = 1, ItemTotal = 10m, CreatedBy = "test" }]
    };

    private static OrderAmendmentRefundAuthoritySnapshot NoRefundAuthority() =>
        new(new AccountMoney("CHF"), new Dictionary<Guid, long>());
}
