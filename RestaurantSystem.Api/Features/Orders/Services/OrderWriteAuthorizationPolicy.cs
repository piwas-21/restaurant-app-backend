using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Shared role policy for order writes and their server-projected actions.</summary>
public static class OrderWriteAuthorizationPolicy
{
    public static OrderWriteAuthorizationDecision ForStatus(
        UserRole? role, Order order, OrderStatus target)
    {
        if (role != UserRole.Server || IsAllowedServerHandoff(order, target))
        {
            return OrderWriteAuthorizationDecision.Allow();
        }

        return OrderWriteAuthorizationDecision.Deny(
            ErrorCodes.KitchenRoleRequired,
            "Only kitchen staff may prepare or mark orders ready.");
    }

    public static OrderWriteAuthorizationDecision ForPayment(UserRole? role) =>
        role is UserRole.Admin or UserRole.Cashier
            ? OrderWriteAuthorizationDecision.Allow()
            : OrderWriteAuthorizationDecision.Deny(
                ErrorCodes.CashierRequired,
                "Only a cashier or admin may record an order payment.");

    public static OrderWriteAuthorizationDecision ForCancellation(UserRole? role, Order order) =>
        role == UserRole.Server && order.IsKitchenReleased
            ? OrderWriteAuthorizationDecision.Deny(
                ErrorCodes.KitchenRoleRequired,
                "A server cannot cancel an order after it has been sent to the kitchen.")
            : OrderWriteAuthorizationDecision.Allow();

    public static OrderWriteAuthorizationDecision ForKitchenPrint(UserRole? role, Order order)
    {
        if (role == UserRole.Server)
        {
            return OrderWriteAuthorizationDecision.Deny(
                ErrorCodes.KitchenRoleRequired,
                "Only kitchen staff may print kitchen work.");
        }

        return order.IsKitchenReleased
            ? OrderWriteAuthorizationDecision.Allow()
            : OrderWriteAuthorizationDecision.Deny(
                ErrorCodes.KitchenReleaseRequired,
                "Release the order before printing kitchen work.");
    }

    private static bool IsAllowedServerHandoff(Order order, OrderStatus target) =>
        (order.Status, target, order.Type) switch
        {
            (OrderStatus.Ready, OrderStatus.Completed, not OrderType.Delivery) => true,
            (OrderStatus.Ready, OrderStatus.OutForDelivery, OrderType.Delivery) => true,
            (OrderStatus.OutForDelivery, OrderStatus.Completed, _) => true,
            _ => false
        };
}

public readonly record struct OrderWriteAuthorizationDecision(
    bool Allowed, string? ErrorCode, string? Message)
{
    public static OrderWriteAuthorizationDecision Allow() => new(true, null, null);

    public static OrderWriteAuthorizationDecision Deny(string errorCode, string message) =>
        new(false, errorCode, message);
}
