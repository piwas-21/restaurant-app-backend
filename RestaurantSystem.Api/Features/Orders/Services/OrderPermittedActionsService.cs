using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public sealed class OrderPermittedActionsService : IOrderPermittedActionsService
{
    private static readonly OrderAction[] ActionOrder =
    [
        OrderAction.Accept,
        OrderAction.StartPreparing,
        OrderAction.MarkReady,
        OrderAction.HandOver,
        OrderAction.CollectPayment,
        OrderAction.AddOperationalNote,
        OrderAction.PrintKitchen,
        OrderAction.PrintReceipt,
        OrderAction.RefundPayment,
        OrderAction.CancelOrder,
        OrderAction.MarkUrgent
    ];

    private readonly ICurrentUserService _currentUser;

    public OrderPermittedActionsService(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    public IReadOnlyList<OrderPermittedActionDto> GetPermittedActions(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return ActionOrder.Select(action => Evaluate(action, order)).ToList();
    }

    private OrderPermittedActionDto Evaluate(OrderAction action, Order order)
    {
        var requiresReason = action is OrderAction.RefundPayment
            or OrderAction.CancelOrder
            or OrderAction.MarkUrgent;

        var (allowed, reasonCode) = action switch
        {
            OrderAction.Accept => order.IsKitchenReleased
                ? StatusAction(order, OrderStatus.Confirmed)
                : (false, OrderActionReasonCodes.KitchenReleaseRequired),
            OrderAction.StartPreparing => StatusAction(order, OrderStatus.Preparing),
            OrderAction.MarkReady => StatusAction(order, OrderStatus.Ready),
            OrderAction.HandOver => HandOverAction(order),
            OrderAction.CollectPayment => CollectPaymentAction(order),
            OrderAction.AddOperationalNote => RoleAction(
                _currentUser.IsAdmin || _currentUser.Role == UserRole.Cashier,
                OrderActionReasonCodes.AdminOrCashierRequired),
            OrderAction.PrintKitchen => order.IsKitchenReleased
                ? RoleAction(_currentUser.IsStaff, OrderActionReasonCodes.StaffRequired)
                : (false, OrderActionReasonCodes.KitchenReleaseRequired),
            OrderAction.PrintReceipt => RoleAction(_currentUser.IsStaff, OrderActionReasonCodes.StaffRequired),
            OrderAction.RefundPayment => RefundPaymentAction(order),
            OrderAction.CancelOrder => CancelAction(order),
            OrderAction.MarkUrgent => RoleAction(_currentUser.IsStaff, OrderActionReasonCodes.StaffRequired),
            _ => (false, OrderActionReasonCodes.UnsupportedAction)
        };

        return new OrderPermittedActionDto
        {
            Action = action.ToString(),
            Allowed = allowed,
            ReasonCode = allowed ? null : reasonCode,
            RequiresReason = requiresReason
        };
    }

    private (bool Allowed, string? ReasonCode) StatusAction(Order order, OrderStatus target)
    {
        var role = RoleAction(_currentUser.IsStaff, OrderActionReasonCodes.StaffRequired);
        if (!role.Allowed)
        {
            return role;
        }

        if (!OrderStatusTransitions.IsValid(order.Status, target))
        {
            return (false, OrderActionReasonCodes.InvalidStatusTransition);
        }

        // Confirming an order while Stripe still reports an in-flight payment is refused by the
        // status handler. The action projection must not invite the cashier to click it.
        if (target == OrderStatus.Confirmed && OnlinePaymentIntent.IsAwaitingPayment(order))
        {
            return (false, OrderActionReasonCodes.OnlinePaymentPending);
        }

        return (true, null);
    }

    private (bool Allowed, string? ReasonCode) CancelAction(Order order)
    {
        var role = RoleAction(_currentUser.IsStaff, OrderActionReasonCodes.StaffRequired);
        if (!role.Allowed)
        {
            return role;
        }

        // The cancel endpoint intentionally has its own terminal guard rather than consulting the
        // transition table: it is the existing write contract, and old rows may contain a status
        // value that the lifecycle table cannot reach. Mirror that endpoint without inventing a
        // new transition rule here.
        return order.Status is OrderStatus.Completed or OrderStatus.Cancelled
            ? (false, OrderActionReasonCodes.InvalidStatusTransition)
            : (true, null);
    }

    private (bool Allowed, string? ReasonCode) HandOverAction(Order order)
    {
        OrderStatus? target = null;
        if (order.Status == OrderStatus.OutForDelivery)
        {
            target = OrderStatus.Completed;
        }
        else if (order.Type == OrderType.Delivery && order.Status == OrderStatus.Ready)
        {
            target = OrderStatus.OutForDelivery;
        }
        else if (order.Status == OrderStatus.Ready)
        {
            target = OrderStatus.Completed;
        }

        if (target is null)
        {
            return (false, OrderActionReasonCodes.InvalidStatusTransition);
        }

        return StatusAction(order, target.Value);
    }

    private (bool Allowed, string? ReasonCode) CollectPaymentAction(Order order)
    {
        var role = RoleAction(_currentUser.IsStaff, OrderActionReasonCodes.StaffRequired);
        if (!role.Allowed)
        {
            return role;
        }

        if (OnlinePaymentIntent.IsAwaitingPayment(order))
        {
            return (false, OrderActionReasonCodes.OnlinePaymentPending);
        }

        if (OrderSettlementEligibility.CanCollect(order))
        {
            return (true, null);
        }

        if (OrderSettlementEligibility.IsTerminalReversal(order))
        {
            return (false, OrderActionReasonCodes.SettlementClosed);
        }

        return (false, OrderActionReasonCodes.NoOutstandingBalance);
    }

    private (bool Allowed, string? ReasonCode) RefundPaymentAction(Order order)
    {
        var role = RoleAction(_currentUser.IsAdmin, OrderActionReasonCodes.AdminRequired);
        if (!role.Allowed)
        {
            return role;
        }

        var completed = order.Payments.Where(payment =>
            payment.Status == PaymentStatus.Completed && !payment.IsRefunded).ToList();
        if (completed.Any(payment => !TenderCustody.IsHeldByGateway(payment)))
        {
            return (true, null);
        }

        if (completed.Any(TenderCustody.IsHeldByGateway))
        {
            return (false, OrderActionReasonCodes.GatewayCustody);
        }

        return (false, OrderActionReasonCodes.NoRefundableTender);
    }

    private static (bool Allowed, string? ReasonCode) RoleAction(bool allowed, string reasonCode) =>
        allowed ? (true, null) : (false, reasonCode);
}

/// <summary>Stable machine-readable reasons for denied order actions.</summary>
public static class OrderActionReasonCodes
{
    public const string StaffRequired = "StaffRequired";
    public const string KitchenReleaseRequired = ErrorCodes.KitchenReleaseRequired;
    public const string AdminRequired = "AdminRequired";
    public const string AdminOrCashierRequired = "AdminOrCashierRequired";
    public const string InvalidStatusTransition = "InvalidStatusTransition";
    public const string OnlinePaymentPending = "OnlinePaymentPending";
    public const string SettlementClosed = "SettlementClosed";
    public const string NoOutstandingBalance = "NoOutstandingBalance";
    public const string GatewayCustody = "GatewayCustody";
    public const string NoRefundableTender = "NoRefundableTender";
    public const string UnsupportedAction = "UnsupportedAction";
}
