using System.Linq.Expressions;
using RestaurantSystem.Domain.Common.Enums;

using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

/// <summary>Shared read/write close predicates for explicit table service sessions.</summary>
public static class TableServiceSessionCloseRules
{
    public static decimal EffectiveOutstanding(decimal total, decimal billingCreditAmount, decimal totalPaid) =>
        Math.Max(0m, total - billingCreditAmount - totalPaid);

    public static TableServiceSessionOrderState FromCharge(
        OrderStatus status,
        decimal total,
        decimal billingCreditAmount,
        decimal totalPaid,
        bool isFullyRefunded = false) =>
        new(status, EffectiveOutstanding(total, billingCreditAmount, totalPaid), isFullyRefunded);

    internal static IQueryable<TableServiceCloseCharge> SelectCloseCharges(this IQueryable<Order> orders) =>
        orders.Select(order => new TableServiceCloseCharge(order.Status, order.Total,
            order.BillingCreditAmount, order.TotalPaid, order.PaymentStatus, order.OrderNumber,
            order.TableId, order.TableNumber));

    /// <summary>
    /// Removes only rows whose unassigned table association was archived by recovery. The order,
    /// its financial evidence and its normal cashier/kitchen projections remain available.
    /// </summary>
    public static IQueryable<Order> ExcludeArchivedLegacyOccupancy(
        IQueryable<Order> orders,
        IQueryable<TableOccupancyRecoveryDisposition> dispositions) =>
        orders.Where(order => !dispositions.Any(disposition =>
            disposition.OrderId == order.Id && disposition.WasLegacyUnassigned));

    /// <summary>
    /// Finds unresolved old rounds for a known physical table by stable ID or exact numeric-label
    /// fallback. Rows with neither identity remain standalone work and are never assigned here.
    /// </summary>
    public static IQueryable<Order> ForUnassignedSession(
        IQueryable<Order> orders,
        IQueryable<TableOccupancyRecoveryDisposition> dispositions,
        Guid? tableId,
        int? tableNumber)
    {
        IQueryable<Order> matching;
        if (tableId.HasValue)
        {
            matching = orders.Where(order => order.TableId == tableId
                || (!order.TableId.HasValue && tableNumber.HasValue
                    && order.TableNumber == tableNumber));
        }

        else matching = tableNumber.HasValue
            ? orders.Where(order => !order.TableId.HasValue && order.TableNumber == tableNumber)
            : orders.Where(_ => false);

        return ExcludeArchivedLegacyOccupancy(matching, dispositions);
    }

    internal static Expression<Func<Order, bool>> BlockingLegacyQuery(decimal paymentTolerance) => order =>
        order.Status != OrderStatus.Completed && order.Status != OrderStatus.Cancelled
        || order.Status == OrderStatus.Completed && order.PaymentStatus != PaymentStatus.Refunded
            && order.Total - order.BillingCreditAmount - order.TotalPaid > paymentTolerance;

    public static bool IsBlockingLegacyOrder(
        TableServiceSessionOrderState order, decimal paymentTolerance) =>
        order.Status is not OrderStatus.Completed and not OrderStatus.Cancelled
        || order.Status == OrderStatus.Completed && Outstanding(order) > paymentTolerance;

    public static bool IsUnresolvedMemberOrder(TableServiceSessionOrderState order) =>
        order.Status is not OrderStatus.Completed and not OrderStatus.Cancelled;

    public static decimal Outstanding(TableServiceSessionOrderState order) =>
        order.Status == OrderStatus.Cancelled || order.IsFullyRefunded
            ? 0m : Math.Max(0m, order.RemainingAmount);

    public static decimal Outstanding(Order order) => Outstanding(FromCharge(
        order.Status, order.Total, order.BillingCreditAmount, order.TotalPaid,
        order.PaymentStatus == PaymentStatus.Refunded));

    public static decimal Outstanding(TableOccupancyRecoveryDisposition disposition) => Outstanding(FromCharge(
        disposition.OriginalStatus, disposition.OriginalTotal, disposition.OriginalBillingCreditAmount,
        disposition.OriginalTotalPaid, disposition.OriginalPaymentStatus == PaymentStatus.Refunded));

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
    OrderStatus Status, decimal RemainingAmount, bool IsFullyRefunded = false);

public sealed record TableServiceSessionCloseAssessment(
    int LegacyActiveOrderCount,
    decimal Outstanding,
    int UnresolvedMemberOrderCount,
    bool CanClose);

internal sealed record TableServiceCloseCharge(
    OrderStatus Status, decimal Total, decimal BillingCreditAmount, decimal TotalPaid,
    PaymentStatus PaymentStatus, string OrderNumber, Guid? TableId, int? TableNumber)
{
    internal TableServiceSessionOrderState ToState() => TableServiceSessionCloseRules.FromCharge(
        Status, Total, BillingCreditAmount, TotalPaid, PaymentStatus == PaymentStatus.Refunded);
}
