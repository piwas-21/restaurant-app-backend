using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderAmendmentLoyaltyResultFactoryTests
{
    [Fact]
    public void Snapshotless_resolved_operation_accepts_only_null_or_canonical_empty_plan()
    {
        var operation = new OrderAmendmentResolutionOperation
        {
            Currency = "CHF",
            State = OrderAmendmentResolutionOperationState.Resolved,
            CreatedBy = nameof(OrderAmendmentLoyaltyResultFactoryTests)
        };

        Validate(operation, plan: null);
        Validate(operation, OrderAmendmentLoyaltyPlan.Empty("CHF"));

        var populatedWithoutSnapshot = OrderAmendmentLoyaltyPlan.Empty("CHF") with
        {
            CandidatePoints = 1
        };

        Assert.Throws<ConflictException>(() => Validate(operation, populatedWithoutSnapshot));
    }

    private static void Validate(
        OrderAmendmentResolutionOperation operation,
        OrderAmendmentLoyaltyPlan? plan) =>
        OrderAmendmentLoyaltyResultFactory.ValidateSettledJournalEvidence(
            operation, plan, [], [], [], [], result: null);
}
