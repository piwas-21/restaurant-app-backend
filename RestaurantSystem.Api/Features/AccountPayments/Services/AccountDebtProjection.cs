using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Projects frozen charges before claims; historical tenders remain contributions.</summary>
internal static class AccountDebtProjection
{
    internal static AccountDebtSnapshot Project(
        IReadOnlyList<Order> orders, AccountMoney money,
        IReadOnlyList<AccountDebtSegment> captured, IReadOnlyList<AccountDebtSegment> reserved,
        IReadOnlyList<OrderAmendment>? amendments = null)
    {
        RequireUniqueOrders(orders);
        var dueBeforeLedger = ProjectOrderDebt(orders, money, captured, reserved);
        var amendmentAdjusted = AccountDebtAmendmentProjection.ExcludeVoidedUnits(
            orders, dueBeforeLedger, amendments ?? []);
        var outstanding = AccountDebtMath.Subtract(amendmentAdjusted, captured);
        var available = AccountDebtMath.Subtract(outstanding, reserved);
        return new(outstanding, available, AccountDebtMath.Total(outstanding), AccountDebtMath.Total(reserved));
    }

    private static void RequireUniqueOrders(IReadOnlyList<Order> orders)
    {
        if (orders.Select(order => order.Id).Distinct().Count() != orders.Count)
            throw new ConflictException("The account contains duplicate order identities.");
    }

    private static List<AccountDebtSegment> ProjectOrderDebt(
        IReadOnlyList<Order> orders,
        AccountMoney money,
        IReadOnlyList<AccountDebtSegment> captured,
        IReadOnlyList<AccountDebtSegment> reserved)
    {
        var due = new List<AccountDebtSegment>();
        foreach (var order in orders.OrderBy(order => order.OrderDate).ThenBy(order => order.Id))
            ProjectOrder(order, money, captured, reserved, due);
        return due;
    }

    private static void ProjectOrder(
        Order order,
        AccountMoney money,
        IReadOnlyList<AccountDebtSegment> captured,
        IReadOnlyList<AccountDebtSegment> reserved,
        List<AccountDebtSegment> due)
    {
        var applied = AccountDebtMath.Total(captured.Where(value => value.OrderId == order.Id));
        var claimed = AccountDebtMath.Total(reserved.Where(value => value.OrderId == order.Id));
        var hasRefund = HasRefundEvidence(order);
        var fullyRefunded = HasProvenFullRefund(order);
        if (hasRefund && !fullyRefunded)
            throw new ConflictException("Partial or unresolved refunds require account reconciliation.");
        if (IsWholeOrderReversal(order) || fullyRefunded)
        {
            RequireNoLedgerValueOnReversedOrder(order, money, applied, claimed);
            return;
        }

        RequireCollectableOrder(order);
        ProjectUnreconciledDebt(order, money, applied, due);
    }

    private static bool IsWholeOrderReversal(Order order) =>
        order.Status is OrderStatus.Cancelled or OrderStatus.Refunded
        || order.PaymentStatus == PaymentStatus.Refunded;

    private static void RequireNoLedgerValueOnReversedOrder(
        Order order, AccountMoney money, long applied, long claimed)
    {
        if (applied > 0 || claimed > 0 || NetCaptured(order, money) > 0)
            throw new ConflictException("A reversed order still has account allocations. Reconciliation is required.");
    }

    private static void RequireCollectableOrder(Order order)
    {
        if (order.ExternalReference is not null)
            throw new ConflictException("Marketplace-held payments cannot be collected through a table account.");
        if (order.Payments.Any(payment => payment.PaymentMethod == PaymentMethod.OnlinePayment
                && payment.Status == PaymentStatus.Processing))
            throw new ConflictException("An existing order checkout must be resolved before account collection.");
    }

    private static void ProjectUnreconciledDebt(
        Order order, AccountMoney money, long applied, List<AccountDebtSegment> due)
    {
        var charge = money.ToMinor(order.Total);
        var paid = NetCaptured(order, money);
        if (applied > paid || paid > charge)
            throw new ConflictException("The order's tender ledger requires reconciliation before account collection.");
        AddUnpaidOrderLines(order, money, charge, paid - applied, due);
    }

    private static void AddUnpaidOrderLines(
        Order order, AccountMoney money, long charge, long historical, List<AccountDebtSegment> due)
    {
        var roots = order.Items.Where(item => item.ParentOrderItemId is null)
            .OrderBy(item => item.Id).ToArray();
        if (roots.Length == 0)
        {
            due.AddRange(AccountDebtMath.CreateUnitemized(order.Id, charge, historical));
            return;
        }
        if (roots.Any(item => item.Quantity <= 0 || item.OrderId != order.Id))
            throw new ConflictException("The account contains an invalid frozen order line.");
        AddLineDebts(order, roots, money, charge, historical, due);
    }

    private static void AddLineDebts(
        Order order,
        OrderItem[] roots,
        AccountMoney money,
        long charge,
        long historical,
        List<AccountDebtSegment> due)
    {
        var weights = roots.Select(item => money.ToMinor(item.ItemTotal)).ToArray();
        if (weights.All(weight => weight == 0))
            weights = roots.Select(item => (long)item.Quantity).ToArray();
        var lineCharges = AccountShareMath.Weighted(charge, weights);
        for (var index = 0; index < roots.Length; index++)
        {
            var contribution = Math.Min(historical, lineCharges[index]);
            historical -= contribution;
            due.AddRange(AccountDebtMath.CreateLine(
                order.Id, roots[index].Id, roots[index].Quantity, lineCharges[index], contribution));
        }
    }

    private static bool HasRefundEvidence(Order order) =>
        order.PaymentStatus is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded
        || order.Payments.Any(payment => payment.IsRefunded || payment.RefundedAmount > 0
            || payment.Status is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded);

    private static bool HasProvenFullRefund(Order order)
    {
        var gross = order.Payments.Where(payment => payment.Status.IsCaptured()).Sum(payment => payment.Amount);
        var wholeOrderReversed = order.Status is OrderStatus.Cancelled or OrderStatus.Refunded
            || order.PaymentStatus == PaymentStatus.Refunded;
        return wholeOrderReversed && gross > 0 && Refunded(order) == gross;
    }

    private static decimal Refunded(Order order) => order.Payments.Sum(payment => payment.RefundedAmount
        ?? (payment.IsRefunded || payment.Status == PaymentStatus.Refunded ? payment.Amount : 0m));

    private static long NetCaptured(Order order, AccountMoney money)
    {
        var captured = money.ToMinor(order.Payments
            .Where(payment => payment.Status.IsCaptured()).Sum(payment => payment.Amount) - Refunded(order));
        if (money.ToMinor(order.TotalPaid) > captured)
            throw new ConflictException("The order summary includes paid money without captured tender evidence.");
        return captured;
    }
}

internal sealed record AccountDebtSnapshot(
    IReadOnlyList<AccountDebtSegment> Outstanding,
    IReadOnlyList<AccountDebtSegment> Available,
    long OutstandingMinor,
    long ReservedMinor)
{
    internal long AvailableMinor => AccountDebtMath.Total(Available);
}
