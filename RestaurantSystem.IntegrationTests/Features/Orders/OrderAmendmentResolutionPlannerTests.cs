using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderAmendmentResolutionPlannerTests
{
    [Fact]
    public void Two_successive_distinct_removals_refund_only_each_frozen_payer_scope()
    {
        var fixture = CreateFixture();
        var firstAmendment = CreateAmendment(fixture, 1_500);
        var first = Build(fixture, firstAmendment, [Void(fixture.FirstItemId)], [],
            new Dictionary<Guid, long>());

        first.RefundMinor.Should().Be(400);
        first.UnpaidWaivedMinor.Should().Be(1_100);
        first.Legs.Should().ContainSingle().Which.Scopes.Should().ContainSingle()
            .Which.OrderItemId.Should().Be(fixture.FirstItemId);

        ApplyFirstResolution(fixture);
        var secondAmendment = CreateAmendment(fixture, 1_500);
        var second = Build(fixture, secondAmendment, [Void(fixture.SecondItemId)],
            fixture.Reversals, new Dictionary<Guid, long> { [fixture.FirstPayment.Id] = 400 });

        second.RefundMinor.Should().Be(1_500);
        second.UnpaidWaivedMinor.Should().Be(0);
        second.Legs.Should().ContainSingle().Which.Should().Match<OrderAmendmentRefundLegPlan>(leg =>
            leg.Payment.Id == fixture.SecondPayment.Id
            && leg.Scopes.Count == 1
            && leg.Scopes[0].OrderItemId == fixture.SecondItemId
            && leg.Scopes[0].StartOrdinal == 1
            && leg.Scopes[0].AmountMinor == 1_500);
    }

    [Fact]
    public void A_previously_refunded_source_ordinal_cannot_be_refunded_again()
    {
        var fixture = CreateFixture();
        ApplyFirstResolution(fixture);
        var amendment = CreateAmendment(fixture, 1_500);

        var error = Record.Exception(() => Build(fixture, amendment,
            [Void(fixture.FirstItemId)], fixture.Reversals,
            new Dictionary<Guid, long> { [fixture.FirstPayment.Id] = 400 }));

        error.Should().BeOfType<ConflictException>()
            .Which.Message.Should().Contain("already has captured refund evidence");
    }

    [Theory]
    [InlineData(1, PaymentStatus.PartiallyRefunded)]
    [InlineData(4, PaymentStatus.Refunded)]
    public void Refunds_without_a_resolved_amendment_evidence_snapshot_fail_closed(
        decimal amount, PaymentStatus status)
    {
        var fixture = CreateFixture();
        fixture.FirstPayment.RefundedAmount = amount;
        fixture.FirstPayment.Status = status;
        fixture.FirstPayment.IsRefunded = status == PaymentStatus.Refunded;
        var amendment = CreateAmendment(fixture, 1_500);

        var error = Record.Exception(() => Build(fixture, amendment,
            [Void(fixture.FirstItemId)], [], new Dictionary<Guid, long>()));

        error.Should().BeOfType<ConflictException>()
            .Which.Message.Should().Contain("lacks exact resolved amendment evidence");
    }

    [Fact]
    public void Loyalty_ledger_history_without_matching_order_summary_fails_closed()
    {
        var fixture = CreateFixture();
        var amendment = CreateAmendment(fixture, 1_500);

        var error = Record.Exception(() => Build(fixture, amendment,
            [Void(fixture.FirstItemId)], [], new Dictionary<Guid, long>(),
            hasLoyaltyLedgerHistory: true));

        error.Should().BeOfType<ConflictException>()
            .Which.Message.Should().Contain("Tax or loyalty effects need a frozen compensation review");
    }

    [Fact]
    public void Malformed_current_financial_snapshot_fails_closed()
    {
        var fixture = CreateFixture();
        var amendment = CreateAmendment(fixture, 1_500);
        amendment.FinancialResolutionJson = "{";

        var error = Record.Exception(() => Build(fixture, amendment,
            [Void(fixture.FirstItemId)], [], new Dictionary<Guid, long>()));

        error.Should().BeOfType<ConflictException>();
    }

    [Fact]
    public void One_amendment_preserves_distinct_payer_scopes_for_two_removed_lines()
    {
        var fixture = CreateFixture();
        var amendment = CreateAmendment(fixture, 3_000);

        var plan = Build(fixture, amendment,
            [Void(fixture.FirstItemId), Void(fixture.SecondItemId)], [],
            new Dictionary<Guid, long>());

        plan.CreditMinor.Should().Be(3_000);
        plan.RefundMinor.Should().Be(1_900);
        plan.Legs.Should().HaveCount(2);
        plan.Legs.Select(value => value.Payment.Id).Should().BeEquivalentTo(
            [fixture.FirstPayment.Id, fixture.SecondPayment.Id]);
        plan.Legs.Single(value => value.Payment.Id == fixture.FirstPayment.Id)
            .Scopes.Single().Should().Match<OrderAmendmentRefundScope>(scope =>
                scope.OrderItemId == fixture.FirstItemId && scope.AmountMinor == 400);
        plan.Legs.Single(value => value.Payment.Id == fixture.SecondPayment.Id)
            .Scopes.Single().Should().Match<OrderAmendmentRefundScope>(scope =>
                scope.OrderItemId == fixture.SecondItemId && scope.AmountMinor == 1_500);
    }

    [Fact]
    public void Stripe_refund_currency_is_compared_as_an_iso_code_not_provider_casing()
    {
        var context = new AmendmentRefundProviderContext("acct_test", LiveMode: false);
        var operationId = Guid.NewGuid();
        var legId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var metadata = new Dictionary<string, string>
        {
            [StripeOrderAmendmentRefundProvider.SchemaKey] = StripeOrderAmendmentRefundProvider.SchemaVersion,
            [StripeOrderAmendmentRefundProvider.OperationKey] = operationId.ToString("D"),
            [StripeOrderAmendmentRefundProvider.LegKey] = legId.ToString("D"),
            [StripeOrderAmendmentRefundProvider.AttemptKey] = attemptId.ToString("D")
        };
        var providerRefund = new AmendmentRefundEvidence("re_test", "ch_test", "pi_test",
            400, "chf", "succeeded", context, metadata);

        var action = () => OrderAmendmentRefundProviderProof.ValidateIdentity(
            providerRefund, context, "ch_test", "pi_test", "CHF");

        action.Should().NotThrow();
    }

    private static OrderAmendmentResolutionPlan Build(
        Fixture fixture, OrderAmendment amendment,
        IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        IReadOnlyList<AccountPaymentAllocationReversal> reversals,
        IReadOnlyDictionary<Guid, long> priorRefunds,
        bool hasLoyaltyLedgerHistory = false) =>
        OrderAmendmentResolutionPlanner.Build(fixture.Source, amendment,
            new OrderAmendmentResolutionQuoteRequest
            {
                ClientOperationId = Guid.NewGuid(),
                ExpectedOrderVersion = fixture.Source.Version,
                ExpectedAccountRevision = fixture.Session.AccountRevision,
                Currency = "CHF"
            }, changes, fixture.Attempts, [], reversals, priorRefunds,
            new AccountMoney("CHF"), hasLoyaltyLedgerHistory);

    private static OrderAmendmentChangeSnapshot Void(Guid itemId) => new(itemId,
        OrderAmendmentChangeKind.Void, 1, 1, false,
        new OrderItemDto { Id = itemId, Quantity = 1 }, null);

    private static OrderAmendment CreateAmendment(Fixture fixture, long creditMinor) => new()
    {
        Id = Guid.NewGuid(),
        SourceOrderId = fixture.Source.Id,
        ServiceSessionId = fixture.Session.Id,
        State = OrderAmendmentState.Committed,
        FinancialResolutionJson = OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto(
            "CHF", 0, creditMinor, -creditMinor, creditMinor,
            OrderAmendmentFinancialResolutionStatus.Pending,
            OrderAmendmentCreditState.PendingAllocationReview,
            OrderAmendmentLoyaltyState.None,
            OrderAmendmentRefundState.PendingTillRefund)),
        CreatedBy = "test"
    };

    private static void ApplyFirstResolution(Fixture fixture)
    {
        fixture.FirstPayment.RefundedAmount = 4m;
        fixture.FirstPayment.IsRefunded = true;
        fixture.FirstPayment.Status = PaymentStatus.Refunded;
        fixture.Source.BillingCreditAmount = 15m;
        fixture.Source.TotalPaid = 15m;
        fixture.Source.RemainingAmount = 0m;
        fixture.Source.Version++;
        fixture.Session.AccountRevision++;
        fixture.Reversals.Add(new AccountPaymentAllocationReversal
        {
            Id = Guid.NewGuid(),
            AllocationId = fixture.FirstAllocation.Id,
            RefundLegId = Guid.NewGuid(),
            OrderId = fixture.Source.Id,
            OrderItemId = fixture.FirstItemId,
            StartOrdinal = 1,
            UnitCount = 1,
            MinorPerUnit = 400,
            AmountMinor = 400,
            Currency = "CHF",
            ActorUserId = Guid.NewGuid(),
            ActorRole = nameof(UserRole.Admin),
            CreatedBy = "test"
        });
    }

    private static Fixture CreateFixture()
    {
        var sessionId = Guid.NewGuid();
        var source = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "A-1001",
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            Version = 1,
            Total = 30m,
            TotalPaid = 19m,
            RemainingAmount = 11m,
            ServiceSessionId = sessionId,
            ServiceSession = new TableServiceSession
            {
                Id = sessionId,
                Currency = "CHF",
                Status = TableServiceSessionStatus.Open,
                AccountRevision = 4,
                CreatedBy = "test"
            },
            CreatedBy = "test"
        };
        var firstItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = source.Id,
            Quantity = 1,
            ItemTotal = 15m,
            CreatedBy = "test"
        };
        var secondItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = source.Id,
            Quantity = 1,
            ItemTotal = 15m,
            CreatedBy = "test"
        };
        source.Items = [firstItem, secondItem];
        var firstPayment = Payment(source.Id, 4m);
        var secondPayment = Payment(source.Id, 15m);
        source.Payments = [firstPayment, secondPayment];
        var firstAttempt = Attempt(sessionId, source.Id, firstItem.Id, firstPayment.Id, 400);
        var secondAttempt = Attempt(sessionId, source.Id, secondItem.Id, secondPayment.Id, 1_500);
        return new Fixture(source, source.ServiceSession, firstItem.Id, secondItem.Id,
            firstPayment, secondPayment, firstAttempt.Allocations.Single(),
            [firstAttempt, secondAttempt], []);
    }

    private static OrderPayment Payment(Guid orderId, decimal amount) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Amount = amount,
        PaymentMethod = PaymentMethod.Cash,
        Currency = "CHF",
        Status = PaymentStatus.Completed,
        CreatedBy = "test"
    };

    private static AccountPaymentAttempt Attempt(Guid sessionId, Guid orderId,
        Guid itemId, Guid paymentId, long amountMinor)
    {
        var attemptId = Guid.NewGuid();
        var allocation = new AccountPaymentAllocation
        {
            Id = Guid.NewGuid(),
            AttemptId = attemptId,
            OrderId = orderId,
            OrderItemId = itemId,
            OrderPaymentId = paymentId,
            StartOrdinal = 1,
            UnitCount = 1,
            MinorPerUnit = amountMinor,
            AmountMinor = amountMinor,
            CreatedBy = "test"
        };
        return new AccountPaymentAttempt
        {
            Id = attemptId,
            ServiceSessionId = sessionId,
            State = AccountPaymentState.Captured,
            PaymentMethod = PaymentMethod.Cash,
            Currency = "CHF",
            AmountMinor = amountMinor,
            Allocations = [allocation],
            CreatedBy = "test"
        };
    }

    private sealed record Fixture(
        Order Source,
        TableServiceSession Session,
        Guid FirstItemId,
        Guid SecondItemId,
        OrderPayment FirstPayment,
        OrderPayment SecondPayment,
        AccountPaymentAllocation FirstAllocation,
        IReadOnlyList<AccountPaymentAttempt> Attempts,
        List<AccountPaymentAllocationReversal> Reversals);
}
