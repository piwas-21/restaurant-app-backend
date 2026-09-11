using System.Linq.Expressions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Decides whether the staff till may collect another tender for an order.
/// </summary>
/// <remarks>
/// Fulfilment and settlement are independent: a completed service can still be unpaid.
/// Cancellation, refund and void states are terminal for collection. A void has no distinct
/// persisted enum in this model; it is represented by a cancelled order or a refunded tender.
/// The expression predicates are also used by read queries so SQL filtering cannot drift from
/// the write-side collection rule.
/// </remarks>
public static class OrderSettlementEligibility
{
    private const decimal PaymentTolerance = 0.01m;

    private static readonly Expression<Func<Order, bool>> TerminalReversalPredicate = order =>
        order.Status == OrderStatus.Cancelled ||
        order.Status == OrderStatus.Refunded ||
        order.PaymentStatus == PaymentStatus.Refunded ||
        order.Payments.Any(payment =>
            payment.Status == PaymentStatus.Refunded ||
            payment.Status == PaymentStatus.PartiallyRefunded ||
            payment.IsRefunded ||
            payment.RefundedAmount > 0);

    private static readonly Expression<Func<Order, bool>> OutstandingBalancePredicate = order =>
        order.TotalPaid < order.Total - PaymentTolerance;

    private static readonly Expression<Func<Order, bool>> CanCollectPredicate =
        BuildCanCollectPredicate();

    private static readonly Expression<Func<Order, bool>> OperationalQueuePredicateValue =
        BuildOperationalQueuePredicate();

    private static readonly Func<Order, bool> CanCollectEvaluator = CanCollectPredicate.Compile();

    /// <summary>
    /// True only when the order has an outstanding balance and no terminal reversal state.
    /// </summary>
    public static bool CanCollect(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return CanCollectEvaluator(order);
    }

    /// <summary>
    /// Returns the SQL-translatable collection rule used by the operational orders read.
    /// </summary>
    public static Expression<Func<Order, bool>> CanCollectQuery() => CanCollectPredicate;

    /// <summary>
    /// Returns unfinished orders of any age, plus completed orders that <see cref="CanCollect"/>.
    /// </summary>
    public static Expression<Func<Order, bool>> OperationalQueuePredicate() =>
        OperationalQueuePredicateValue;

    private static Expression<Func<Order, bool>> BuildCanCollectPredicate()
    {
        var parameter = Expression.Parameter(typeof(Order), "order");
        var terminalReversal = ReplaceParameter(TerminalReversalPredicate, parameter);
        var outstandingBalance = ReplaceParameter(OutstandingBalancePredicate, parameter);

        return Expression.Lambda<Func<Order, bool>>(
            Expression.AndAlso(Expression.Not(terminalReversal), outstandingBalance),
            parameter);
    }

    private static Expression<Func<Order, bool>> BuildOperationalQueuePredicate()
    {
        var parameter = Expression.Parameter(typeof(Order), "order");
        var status = Expression.Property(parameter, nameof(Order.Status));
        var unfinished = Expression.AndAlso(
            Expression.Not(ReplaceParameter(TerminalReversalPredicate, parameter)),
            Expression.NotEqual(status, Expression.Constant(OrderStatus.Completed)));
        var completedAndCollectible = Expression.AndAlso(
            Expression.Equal(status, Expression.Constant(OrderStatus.Completed)),
            ReplaceParameter(CanCollectPredicate, parameter));

        return Expression.Lambda<Func<Order, bool>>(
            Expression.OrElse(unfinished, completedAndCollectible),
            parameter);
    }

    private static Expression ReplaceParameter(
        Expression<Func<Order, bool>> source,
        ParameterExpression target)
    {
        return new ParameterReplacementVisitor(source.Parameters[0], target).Visit(source.Body);
    }

    private sealed class ParameterReplacementVisitor(
        ParameterExpression source,
        ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? target : base.VisitParameter(node);
    }
}
