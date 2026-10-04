using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentSourcePaymentSummary
{
    internal static void Recalculate(Order source, AccountMoney money, DateTime now)
    {
        var captured = source.Payments.Where(value => value.Status.IsCaptured())
            .Sum(value => money.ToMinor(value.Amount));
        var refunded = source.Payments.Sum(value => money.ToMinor(value.RefundedAmount ?? 0m));
        var netPaid = checked(captured - refunded);
        var payable = money.ToMinor(source.PayableTotal);
        if (netPaid < 0 || netPaid > payable)
            throw new ConflictException("The resolved refunds do not reconcile to the retained order charge.");
        source.TotalPaid = money.ToMajor(netPaid);
        source.RemainingAmount = money.ToMajor(payable - netPaid);
        var allCapturedRefunded = source.Payments.Where(value => value.Status.IsCaptured())
            .All(value => value.IsRefunded);
        if (payable == 0 && netPaid == 0 && allCapturedRefunded)
            source.PaymentStatus = PaymentStatus.Refunded;
        else if (netPaid == payable)
            source.PaymentStatus = PaymentStatus.Completed;
        else if (netPaid == 0)
            source.PaymentStatus = PaymentStatus.Pending;
        else
            source.PaymentStatus = PaymentStatus.PartiallyPaid;
        source.UpdatedAt = now;
    }
}
