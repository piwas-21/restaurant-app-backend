using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderBillingCreditLoyaltyConsistencyTests
{
    private static readonly Guid OrderId = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    private static readonly Guid AmendmentId = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    private static readonly Guid ActorId = Guid.Parse("b0000000-0000-0000-0000-000000000003");

    [Fact]
    public void Tender_credit_accepts_exactly_posted_loyalty_compensation_evidence()
    {
        var validate = () => OrderBillingCreditConsistency.Validate([new(OrderId, 12.5m)],
            [Amendment(Preview(OrderAmendmentLoyaltyState.Resolved) with { Loyalty = ResolvedLoyalty() })],
            [Credit()]);

        validate.Should().NotThrow();
    }

    [Fact]
    public void Tender_credit_with_resolved_loyalty_but_missing_posting_is_rejected()
    {
        var mismatch = ResolvedLoyalty() with { PostedClawbackPoints = 9 };
        var preview = Preview(OrderAmendmentLoyaltyState.Resolved) with { Loyalty = mismatch };
        var validate = () => OrderBillingCreditConsistency.Validate([new(OrderId, 12.5m)],
            [Amendment(preview)], [Credit()]);

        validate.Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData(OrderBillingEarningDisposition.Unevaluated, true)]
    [InlineData(OrderBillingEarningDisposition.NoCustomerOwnerAtAcceptance, false)]
    [InlineData(OrderBillingEarningDisposition.LoyaltyModuleDisabledAtAcceptance, false)]
    public void Tender_credit_accepts_null_candidate_when_exact_redemption_restoration_is_posted(
        OrderBillingEarningDisposition disposition, bool earningRetired)
    {
        var loyalty = NullCandidateRestoration(disposition, earningRetired);
        var validate = () => OrderBillingCreditConsistency.Validate([new(OrderId, 12.5m)],
            [Amendment(Preview(OrderAmendmentLoyaltyState.Resolved) with { Loyalty = loyalty })],
            [Credit()]);

        validate.Should().NotThrow();
    }

    [Fact]
    public void Tender_credit_rejects_unretired_unknown_candidate_even_when_redemption_was_restored()
    {
        var loyalty = NullCandidateRestoration(OrderBillingEarningDisposition.Unevaluated, earningRetired: false);
        var validate = () => OrderBillingCreditConsistency.Validate([new(OrderId, 12.5m)],
            [Amendment(Preview(OrderAmendmentLoyaltyState.Resolved) with { Loyalty = loyalty })],
            [Credit()]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Tender_credit_rejects_null_candidate_restoration_that_was_not_fully_posted()
    {
        var loyalty = NullCandidateRestoration(OrderBillingEarningDisposition.Unevaluated, earningRetired: true)
            with
        { PostedRestorationPoints = 34 };
        var validate = () => OrderBillingCreditConsistency.Validate([new(OrderId, 12.5m)],
            [Amendment(Preview(OrderAmendmentLoyaltyState.Resolved) with { Loyalty = loyalty })],
            [Credit()]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Tender_credit_rejects_retired_evaluated_candidate()
    {
        var loyalty = EvaluatedLoyalty(candidate: 10, applied: 10, suppressed: 0) with { EarningRetired = true };
        var validate = () => OrderBillingCreditConsistency.Validate([new(OrderId, 12.5m)],
            [Amendment(Preview(OrderAmendmentLoyaltyState.Resolved) with { Loyalty = loyalty })],
            [Credit()]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Tender_credit_rejects_evaluated_award_conservation_above_candidate()
    {
        var loyalty = EvaluatedLoyalty(candidate: 10, applied: 8, suppressed: 3);
        var validate = () => OrderBillingCreditConsistency.Validate([new(OrderId, 12.5m)],
            [Amendment(Preview(OrderAmendmentLoyaltyState.Resolved) with { Loyalty = loyalty })],
            [Credit()]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Tender_credit_cannot_claim_no_loyalty_when_an_obligation_remains()
    {
        var pending = ResolvedLoyalty() with
        {
            State = OrderAmendmentLoyaltyOperationStatus.None,
            EarnedClawbackPoints = 10,
            PostedClawbackPoints = 0
        };
        var preview = Preview(OrderAmendmentLoyaltyState.None) with { Loyalty = pending };
        var validate = () => OrderBillingCreditConsistency.Validate([new(OrderId, 12.5m)],
            [Amendment(preview)], [Credit()]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Historical_suppression_is_reported_on_a_later_zero_effect_amendment()
    {
        var operation = new OrderAmendmentResolutionOperation
        {
            Id = Guid.NewGuid(),
            SourceOrderId = OrderId,
            AmendmentId = AmendmentId,
            State = OrderAmendmentResolutionOperationState.Resolved,
            CreatedBy = "test"
        };
        var plan = new OrderAmendmentLoyaltyPlan("CHF", Guid.NewGuid(), 100, 80, false,
            0, 0, [], [], [], SuppressedPoints: 20);
        var witness = new OrderBillingAwardWitness
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            OwnerLinkId = Guid.NewGuid(),
            Outcome = OrderBillingAwardOutcome.Awarded,
            CandidatePoints = 100,
            AppliedPoints = 80,
            SuppressedPoints = 20,
            EarnedTransactionId = Guid.NewGuid(),
            CreatedBy = "test"
        };
        var loyalty = OrderAmendmentLoyaltyResultFactory.Create(operation,
            new(plan, witness, [], [], [], [], null));
        loyalty.Should().NotBeNull();
        loyalty!.State.Should().Be(OrderAmendmentLoyaltyOperationStatus.None);
        loyalty.SuppressedPoints.Should().Be(20);

        var preview = Preview(OrderAmendmentLoyaltyState.None) with { Loyalty = loyalty };
        var validate = () => OrderBillingCreditConsistency.Validate([new(OrderId, 12.5m)],
            [Amendment(preview)], [Credit()]);

        validate.Should().NotThrow();
    }

    [Fact]
    public void Unretired_unknown_earning_cannot_become_a_resolved_no_award_result()
    {
        var operation = ResolvedOperation();
        var plan = Plan(OrderBillingEarningDisposition.Unevaluated, retired: false);

        var create = () => OrderAmendmentLoyaltyResultFactory.Create(operation,
            new(plan, null, [], [], [], [], null));

        create.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Evaluated_candidate_cannot_be_marked_as_retired()
    {
        var create = () => OrderAmendmentLoyaltyResultFactory.Create(ResolvedOperation(),
            new(Plan(OrderBillingEarningDisposition.Evaluated, retired: true), null, [], [], [], [], null));

        create.Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData(OrderBillingEarningDisposition.NoCustomerOwnerAtAcceptance)]
    [InlineData(OrderBillingEarningDisposition.LoyaltyModuleDisabledAtAcceptance)]
    public void Frozen_known_ineligible_earning_can_remain_unretired(
        OrderBillingEarningDisposition disposition)
    {
        var result = OrderAmendmentLoyaltyResultFactory.Create(ResolvedOperation(),
            new(Plan(disposition, retired: false), null, [], [], [], [], null));

        result.Should().NotBeNull();
        result!.CandidatePoints.Should().BeNull();
        result.EarningDisposition.Should().Be(disposition);
        result.EarningRetired.Should().BeFalse();
    }

    private static OrderAmendmentLoyaltyResultDto ResolvedLoyalty() => new(
        OrderAmendmentLoyaltyOperationStatus.Resolved, false, 100, 80, 20,
        10, 5, 10, 5, null, null);

    private static OrderAmendmentLoyaltyResultDto NullCandidateRestoration(
        OrderBillingEarningDisposition disposition, bool earningRetired) => new(
        OrderAmendmentLoyaltyOperationStatus.Resolved, false, null, 0, 0,
        0, 35, 0, 35, null, null, disposition, earningRetired);

    private static OrderAmendmentLoyaltyResultDto EvaluatedLoyalty(int candidate, int applied, int suppressed) => new(
        OrderAmendmentLoyaltyOperationStatus.Resolved, false, candidate, applied, suppressed,
        10, 0, 10, 0, null, null, OrderBillingEarningDisposition.Evaluated, false);

    private static OrderAmendmentResolutionOperation ResolvedOperation() => new()
    {
        Id = Guid.NewGuid(),
        SourceOrderId = OrderId,
        AmendmentId = AmendmentId,
        State = OrderAmendmentResolutionOperationState.Resolved,
        CreatedBy = "test"
    };

    private static OrderAmendmentLoyaltyPlan Plan(
        OrderBillingEarningDisposition disposition, bool retired) => new(
            "CHF", Guid.NewGuid(), 0, 0, false, 0, 0, [], [], [],
            EarningDisposition: disposition, EarningRetired: retired);

    private static OrderAmendmentFinancialPreviewDto Preview(OrderAmendmentLoyaltyState state) => new(
        "CHF", 0, 1250, -1250, 1250, OrderAmendmentFinancialResolutionStatus.Resolved,
        OrderAmendmentCreditState.Resolved, state, OrderAmendmentRefundState.Resolved);

    private static BillingCreditAmendment Amendment(OrderAmendmentFinancialPreviewDto preview) => new(
        AmendmentId, OrderId, ActorId, "Admin", OrderAmendmentJson.Serialize(preview));

    private static OrderBillingCredit Credit() => new()
    {
        Id = Guid.Parse("b0000000-0000-0000-0000-000000000004"),
        SourceOrderId = OrderId,
        AmendmentId = AmendmentId,
        AmountMinor = 1250,
        Currency = "CHF",
        ActorUserId = ActorId,
        ActorRole = "Admin",
        CreatedBy = ActorId.ToString()
    };
}
