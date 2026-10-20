using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CustomerOrderCancellationPolicyTests
{
    [Fact]
    public void Only_a_verified_unpaid_local_delay_can_be_declined() =>
        CustomerOrderCancellationPolicy.CanDeclineDelay(OrderStatus.PendingApproval,
            OrderConfirmationFlows.Direct, true, true).Should().BeTrue();

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.OutForDelivery)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData((OrderStatus)int.MaxValue)]
    public void Other_or_unknown_order_states_cannot_be_cancelled_by_this_action(OrderStatus status) =>
        CustomerOrderCancellationPolicy.CanDeclineDelay(status,
            OrderConfirmationFlows.Direct, true, true).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(OrderConfirmationFlows.Acknowledge)]
    [InlineData("future")]
    [InlineData("DIRECT")]
    public void Restaurant_acknowledgement_and_unknown_flow_are_not_customer_delay_approval(string? flow) =>
        CustomerOrderCancellationPolicy.CanDeclineDelay(OrderStatus.PendingApproval,
            flow, true, true).Should().BeFalse();

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Unverified_membership_or_financial_clearance_never_grants_the_action(
        bool localStandalone, bool financialClearance) =>
        CustomerOrderCancellationPolicy.CanDeclineDelay(OrderStatus.PendingApproval,
            OrderConfirmationFlows.Direct, localStandalone, financialClearance).Should().BeFalse();
}
