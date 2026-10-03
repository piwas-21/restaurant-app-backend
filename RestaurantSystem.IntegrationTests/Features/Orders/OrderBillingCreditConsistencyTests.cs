using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderBillingCreditConsistencyTests
{
    private static readonly Guid OrderId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid AmendmentId = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Guid ActorId = Guid.Parse("a0000000-0000-0000-0000-000000000003");
    private static readonly Guid SecondAmendmentId = Guid.Parse("a0000000-0000-0000-0000-000000000005");

    [Fact]
    public void Valid_credit_matches_the_resolved_amendment_and_order_aggregate()
    {
        var validate = () => OrderBillingCreditConsistency.Validate(
            [Order(12.50m)], [Amendment()], [Credit()]);

        validate.Should().NotThrow();
    }

    [Fact]
    public void Resolved_credit_without_a_journal_entry_requires_reconciliation()
    {
        var validate = () => OrderBillingCreditConsistency.Validate([Order(12.50m)], [Amendment()], []);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Journal_entry_without_a_committed_amendment_requires_reconciliation()
    {
        var validate = () => OrderBillingCreditConsistency.Validate([Order(12.50m)], [], [Credit()]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Journal_actor_must_match_the_committed_amendment()
    {
        var credit = Credit(actorId: Guid.NewGuid());

        var validate = () => OrderBillingCreditConsistency.Validate([Order(12.50m)], [Amendment()], [credit]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Journal_actor_role_must_match_the_committed_amendment()
    {
        var credit = Credit(actorRole: "Server");

        var validate = () => OrderBillingCreditConsistency.Validate([Order(12.50m)], [Amendment()], [credit]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Journal_currency_must_match_the_resolved_amendment()
    {
        var credit = Credit(currency: "EUR");

        var validate = () => OrderBillingCreditConsistency.Validate([Order(12.50m)], [Amendment()], [credit]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Journal_source_order_must_match_the_committed_amendment()
    {
        var amendment = Amendment() with { SourceOrderId = Guid.NewGuid() };

        var validate = () => OrderBillingCreditConsistency.Validate([Order(12.50m)], [amendment], [Credit()]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Order_credit_aggregate_must_equal_its_minor_unit_journal_sum()
    {
        var validate = () => OrderBillingCreditConsistency.Validate([Order(12.51m)], [Amendment()], [Credit()]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Duplicate_journal_evidence_for_one_amendment_requires_reconciliation()
    {
        var duplicate = Credit(id: Guid.NewGuid());
        var secondAmendment = Amendment() with { Id = SecondAmendmentId };

        var validate = () => OrderBillingCreditConsistency.Validate(
            [Order(25m)], [Amendment(), secondAmendment], [Credit(), duplicate]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Duplicate_committed_amendment_identity_requires_reconciliation()
    {
        var duplicate = Amendment();

        var validate = () => OrderBillingCreditConsistency.Validate(
            [Order(12.50m)], [Amendment(), duplicate], [Credit()]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Zero_credit_ordinary_order_needs_no_amendment_or_journal()
    {
        var validate = () => OrderBillingCreditConsistency.Validate([Order(0m)], [], []);

        validate.Should().NotThrow();
    }

    private static BillingCreditOrder Order(decimal aggregateCredit) => new(OrderId, aggregateCredit);

    private static BillingCreditAmendment Amendment() => new(
        AmendmentId, OrderId, ActorId, "Cashier", OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto(
            "CHF", 0, 1250, -1250, 1250, OrderAmendmentFinancialResolutionStatus.Resolved,
            OrderAmendmentCreditState.BalanceReduction, OrderAmendmentLoyaltyState.None,
            OrderAmendmentRefundState.None)));

    private static OrderBillingCredit Credit(Guid? id = null, Guid? sourceOrderId = null,
        Guid? amendmentId = null, long amountMinor = 1250, string currency = "CHF",
        Guid? actorId = null, string actorRole = "Cashier") => new()
        {
            Id = id ?? Guid.Parse("a0000000-0000-0000-0000-000000000004"),
            SourceOrderId = sourceOrderId ?? OrderId,
            AmendmentId = amendmentId ?? AmendmentId,
            AmountMinor = amountMinor,
            Currency = currency,
            ActorUserId = actorId ?? ActorId,
            ActorRole = actorRole,
            CreatedBy = ActorId.ToString()
        };
}
