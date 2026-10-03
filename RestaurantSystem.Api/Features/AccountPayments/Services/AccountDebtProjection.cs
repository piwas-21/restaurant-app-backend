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
        if (orders.Select(order => order.Id).Distinct().Count() != orders.Count)
            throw new ConflictException("The account contains duplicate order identities.");
        var dueBeforeLedger = new List<AccountDebtSegment>();
        foreach (var order in orders.OrderBy(order => order.OrderDate).ThenBy(order => order.Id))
        {
            var applied = AccountDebtMath.Total(captured.Where(value => value.OrderId == order.Id));
            var claimed = AccountDebtMath.Total(reserved.Where(value => value.OrderId == order.Id));
            var hasRefund = HasRefundEvidence(order);
            var wholeReversal = order.Status is OrderStatus.Cancelled or OrderStatus.Refunded
                || order.PaymentStatus == PaymentStatus.Refunded;
            if (hasRefund && !HasProvenFullRefund(order))
                throw new ConflictException("Partial or unresolved refunds require account reconciliation.");
            if (wholeReversal || HasProvenFullRefund(order))
            {
                if (applied > 0 || claimed > 0 || NetCaptured(order, money) > 0)
                    throw new ConflictException("A reversed order still has account allocations. Reconciliation is required.");
                continue;
            }
            if (order.ExternalReference is not null)
                throw new ConflictException("Marketplace-held payments cannot be collected through a table account.");
            if (order.Payments.Any(payment => payment.PaymentMethod == PaymentMethod.OnlinePayment
                    && payment.Status == PaymentStatus.Processing))
                throw new ConflictException("An existing order checkout must be resolved before account collection.");

            var charge = money.ToMinor(order.Total);
            var paid = NetCaptured(order, money);
            if (applied > paid || paid > charge)
                throw new ConflictException("The order's tender ledger requires reconciliation before account collection.");
            var historical = paid - applied;
            var roots = order.Items.Where(item => item.ParentOrderItemId is null)
                .OrderBy(item => item.Id).ToArray();
            if (roots.Length == 0)
            {
                dueBeforeLedger.AddRange(AccountDebtMath.CreateUnitemized(order.Id, charge, historical));
                continue;
            }
            if (roots.Any(item => item.Quantity <= 0 || item.OrderId != order.Id))
                throw new ConflictException("The account contains an invalid frozen order line.");
            var weights = roots.Select(item => money.ToMinor(item.ItemTotal)).ToArray();
            if (weights.All(weight => weight == 0))
                weights = roots.Select(item => (long)item.Quantity).ToArray();
            var lineCharges = AccountShareMath.Weighted(charge, weights);
            for (var index = 0; index < roots.Length; index++)
            {
                var contribution = Math.Min(historical, lineCharges[index]);
                historical -= contribution;
                dueBeforeLedger.AddRange(AccountDebtMath.CreateLine(
                    order.Id, roots[index].Id, roots[index].Quantity, lineCharges[index], contribution));
            }
        }
        var amendmentAdjusted = AccountDebtAmendmentProjection.ExcludeVoidedUnits(
            orders, dueBeforeLedger, amendments ?? []);
        var outstanding = AccountDebtMath.Subtract(amendmentAdjusted, captured);
        var available = AccountDebtMath.Subtract(outstanding, reserved);
        return new(outstanding, available, AccountDebtMath.Total(outstanding), AccountDebtMath.Total(reserved));
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
