using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

/// <summary>Shared read/write close predicates for explicit table service sessions.</summary>
public static class TableServiceSessionCloseRules
{
    public static bool IsBlockingLegacyOrder(
        TableServiceSessionOrderState order, decimal paymentTolerance) =>
        order.Status is not OrderStatus.Completed and not OrderStatus.Cancelled
        || order.Status == OrderStatus.Completed && order.RemainingAmount > paymentTolerance;

    public static bool IsUnresolvedMemberOrder(TableServiceSessionOrderState order) =>
        order.Status is not OrderStatus.Completed and not OrderStatus.Cancelled;

    public static decimal Outstanding(TableServiceSessionOrderState order) =>
        order.Status == OrderStatus.Cancelled ? 0m : Math.Max(0m, order.RemainingAmount);

    public static TableServiceSessionCloseAssessment Assess(
        IEnumerable<TableServiceSessionOrderState> memberOrders,
        IEnumerable<TableServiceSessionOrderState> legacyOrders,
        decimal paymentTolerance)
    {
        var legacyCount = legacyOrders.Count(order =>
            IsBlockingLegacyOrder(order, paymentTolerance));
        var outstanding = memberOrders.Sum(Outstanding);
        var unresolved = memberOrders.Count(IsUnresolvedMemberOrder);
        return new TableServiceSessionCloseAssessment(
            legacyCount, outstanding, unresolved,
            legacyCount == 0 && outstanding <= paymentTolerance && unresolved == 0);
    }
}

public sealed record TableServiceSessionOrderState(
    OrderStatus Status, decimal RemainingAmount);

public sealed record TableServiceSessionCloseAssessment(
    int LegacyActiveOrderCount,
    decimal Outstanding,
    int UnresolvedMemberOrderCount,
    bool CanClose);
