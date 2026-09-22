using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>Unit coverage for the server-owned detail action contract.</summary>
public class OrderPermittedActionsServiceTests
{
    [Fact]
    public void Returns_every_action_in_stable_order_for_staff()
    {
        var actions = Actions(NewOrder(OrderStatus.Pending), UserRole.Admin);

        actions.Select(action => action.Action).Should().Equal(
            "Accept", "StartPreparing", "MarkReady", "HandOver", "CollectPayment",
            "AddOperationalNote", "PrintKitchen", "PrintReceipt", "RefundPayment",
            "CancelOrder", "MarkUrgent");
        actions.All(action => action.ReasonCode is not null || action.Allowed).Should().BeTrue();
        Action(actions, OrderAction.RefundPayment).RequiresReason.Should().BeTrue();
        Action(actions, OrderAction.CancelOrder).RequiresReason.Should().BeTrue();
        Action(actions, OrderAction.MarkUrgent).RequiresReason.Should().BeTrue();
        actions.Where(action => action.Action is not ("RefundPayment" or "CancelOrder" or "MarkUrgent"))
            .Should().OnlyContain(action => !action.RequiresReason);
    }

    [Fact]
    public void Mirrors_transition_table_and_online_payment_uncertainty()
    {
        var pending = Actions(NewOrder(OrderStatus.Pending), UserRole.Cashier);

        Action(pending, OrderAction.Accept).Allowed.Should().BeTrue();
        Action(pending, OrderAction.StartPreparing).ReasonCode
            .Should().Be(OrderActionReasonCodes.InvalidStatusTransition);

        var awaiting = NewOrder(OrderStatus.Pending);
        awaiting.Payments.Add(new OrderPayment
        {
            PaymentMethod = PaymentMethod.OnlinePayment,
            Status = PaymentStatus.Processing,
            CreatedBy = nameof(OrderPermittedActionsServiceTests),
        });
        var awaitingActions = Actions(awaiting, UserRole.Cashier);

        var awaitingAccept = Action(awaitingActions, OrderAction.Accept);
        awaitingAccept.Allowed.Should().BeFalse();
        awaitingAccept.ReasonCode.Should().Be(OrderActionReasonCodes.OnlinePaymentPending);
        var awaitingCollection = Action(awaitingActions, OrderAction.CollectPayment);
        awaitingCollection.Allowed.Should().BeFalse();
        awaitingCollection.ReasonCode.Should().Be(OrderActionReasonCodes.OnlinePaymentPending);
    }

    [Theory]
    [InlineData(OrderStatus.Preparing, OrderAction.MarkReady)]
    public void Kitchen_actions_are_not_granted_to_server(OrderStatus status, OrderAction action)
    {
        var actions = Actions(NewOrder(status), UserRole.Server);

        Action(actions, action).Allowed.Should().BeFalse();
        Action(actions, action).ReasonCode.Should().Be(OrderActionReasonCodes.KitchenRoleRequired);
    }

    [Theory]
    [InlineData(OrderStatus.Ready, OrderAction.HandOver)]
    [InlineData(OrderStatus.OutForDelivery, OrderAction.HandOver)]
    public void Server_can_only_advance_service_delivery_actions(OrderStatus status, OrderAction action)
    {
        var actions = Actions(NewOrder(status), UserRole.Server);

        Action(actions, action).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Held_staff_order_cannot_accept_or_print_before_release()
    {
        var held = NewOrder(OrderStatus.Pending);
        held.IsKitchenReleased = false;
        var actions = Actions(held, UserRole.Cashier);

        Action(actions, OrderAction.Accept).ReasonCode
            .Should().Be(OrderActionReasonCodes.KitchenReleaseRequired);
        Action(actions, OrderAction.PrintKitchen).ReasonCode
            .Should().Be(OrderActionReasonCodes.KitchenReleaseRequired);
        Action(actions, OrderAction.PrintReceipt).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Uses_settlement_policy_not_a_remaining_amount_shortcut()
    {
        var completedUnpaid = NewOrder(OrderStatus.Completed, total: 50m, totalPaid: 0m);
        Action(Actions(completedUnpaid, UserRole.Cashier), OrderAction.CollectPayment)
            .Allowed.Should().BeTrue();

        var cancelled = NewOrder(OrderStatus.Cancelled, total: 50m, totalPaid: 0m);
        var cancelledCollect = Action(Actions(cancelled, UserRole.Cashier), OrderAction.CollectPayment);
        cancelledCollect.Allowed.Should().BeFalse();
        cancelledCollect.ReasonCode.Should().Be(OrderActionReasonCodes.SettlementClosed);

        var credited = NewOrder(OrderStatus.Completed, total: 50m, totalPaid: 60m);
        var creditedCollect = Action(Actions(credited, UserRole.Cashier), OrderAction.CollectPayment);
        creditedCollect.Allowed.Should().BeFalse();
        creditedCollect.ReasonCode.Should().Be(OrderActionReasonCodes.NoOutstandingBalance);

        var refundedTender = NewOrder(OrderStatus.Completed, total: 50m, totalPaid: 20m);
        refundedTender.Payments.Add(new OrderPayment
        {
            Amount = 20m,
            Status = PaymentStatus.PartiallyRefunded,
            RefundedAmount = 5m,
            CreatedBy = nameof(OrderPermittedActionsServiceTests),
        });
        var refundedCollect = Action(Actions(refundedTender, UserRole.Cashier), OrderAction.CollectPayment);
        refundedCollect.Allowed.Should().BeFalse();
        refundedCollect.ReasonCode.Should().Be(OrderActionReasonCodes.SettlementClosed);
    }

    [Fact]
    public void Refund_requires_admin_and_manual_custody()
    {
        var manual = NewOrder(OrderStatus.Completed, total: 50m, totalPaid: 50m);
        manual.Payments.Add(new OrderPayment
        {
            Amount = 50m,
            Status = PaymentStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            CreatedBy = nameof(OrderPermittedActionsServiceTests)
        });

        Action(Actions(manual, UserRole.Admin), OrderAction.RefundPayment).Allowed.Should().BeTrue();
        var cashierRefund = Action(Actions(manual, UserRole.Cashier), OrderAction.RefundPayment);
        cashierRefund.Allowed.Should().BeFalse();
        cashierRefund.ReasonCode.Should().Be(OrderActionReasonCodes.AdminRequired);

        var gateway = NewOrder(OrderStatus.Completed, total: 50m, totalPaid: 50m);
        gateway.Payments.Add(new OrderPayment
        {
            Amount = 50m,
            Status = PaymentStatus.Completed,
            PaymentMethod = PaymentMethod.OnlinePayment,
            PaymentGateway = "Stripe",
            CreatedBy = nameof(OrderPermittedActionsServiceTests)
        });
        var gatewayRefund = Action(Actions(gateway, UserRole.Admin), OrderAction.RefundPayment);
        gatewayRefund.Allowed.Should().BeFalse();
        gatewayRefund.ReasonCode.Should().Be(OrderActionReasonCodes.GatewayCustody);
    }

    [Fact]
    public void Applies_capabilities_and_never_grants_actions_to_a_customer()
    {
        var order = NewOrder(OrderStatus.Pending);
        var cashier = Actions(order, UserRole.Cashier);
        var server = Actions(order, UserRole.Server);
        var customer = Actions(order, UserRole.Customer);

        Action(cashier, OrderAction.AddOperationalNote).Allowed.Should().BeTrue();
        Action(server, OrderAction.AddOperationalNote).ReasonCode
            .Should().Be(OrderActionReasonCodes.AdminOrCashierRequired);
        customer.All(action => !action.Allowed && action.ReasonCode is not null).Should().BeTrue();
    }

    private static IReadOnlyList<OrderPermittedActionDto> Actions(Order order, UserRole role)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(user => user.Role).Returns(role);
        currentUser.SetupGet(user => user.IsAdmin).Returns(role == UserRole.Admin);
        currentUser.SetupGet(user => user.IsStaff).Returns(role != UserRole.Customer);
        return new OrderPermittedActionsService(currentUser.Object).GetPermittedActions(order);
    }

    private static OrderPermittedActionDto Action(
        IReadOnlyList<OrderPermittedActionDto> actions, OrderAction action) =>
        actions.Single(item => item.Action == action.ToString());

    private static Order NewOrder(
        OrderStatus status, decimal total = 10m, decimal totalPaid = 0m) => new()
        {
            OrderNumber = "UNIT-ACTIONS",
            Type = OrderType.Takeaway,
            Status = status,
            PaymentStatus = totalPaid >= total ? PaymentStatus.Completed : PaymentStatus.Pending,
            Total = total,
            TotalPaid = totalPaid,
            RemainingAmount = total - totalPaid,
            CreatedBy = nameof(OrderPermittedActionsServiceTests),
        };
}
