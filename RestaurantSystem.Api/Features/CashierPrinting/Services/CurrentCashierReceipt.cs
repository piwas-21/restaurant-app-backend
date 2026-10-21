using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.CashierPrinting.Services;

/// <summary>Combines the same current members as the staff browser bill without changing stored orders.</summary>
internal static class CurrentCashierReceipt
{
    internal static OrderDto FromOrders(Guid sourceId, IReadOnlyList<OrderDto> current)
    {
        var source = current.Single(order => order.Id == sourceId);
        var orders = current.Where(order => order.Status is not ("Cancelled" or "Refunded")).ToList();
        if (orders.Count == 0) return CashierReceiptSnapshot.Sanitize(source);
        if (orders.Select(order => order.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            throw new ConflictException("Resolve the bill currency before printing.");
        if (orders.Any(order => order.ExternalOrder is not null))
            throw new ConflictException("Use the marketplace receipt workflow for this order.");
        var due = orders.Sum(order => order.RemainingAmount);
        var paid = orders.Sum(order => order.TotalPaid);
        var receipt = source with
        {
            Items = orders.SelectMany(order => order.Items).ToList(),
            SubTotal = orders.Sum(order => order.SubTotal),
            Tax = orders.Sum(order => order.Tax),
            Discount = orders.Sum(order => order.Discount),
            CustomerDiscountAmount = orders.Sum(order => order.CustomerDiscountAmount),
            FidelityPointsDiscount = orders.Sum(order => order.FidelityPointsDiscount),
            DeliveryFee = orders.Sum(order => order.DeliveryFee),
            Tip = orders.Sum(order => order.Tip),
            Total = orders.Sum(order => order.Total),
            PayableTotal = orders.Sum(order => order.PayableTotal ?? order.Total),
            // Amendment credits are already reflected in the current line amounts.
            BillingCreditAmount = 0,
            BillChargeAmount = orders.Sum(order => order.BillChargeAmount),
            BillCharges = orders.SelectMany(order => order.BillCharges).ToList(),
            PaymentTipMinor = orders.Sum(order => order.PaymentTipMinor),
            TotalPaid = paid,
            RemainingAmount = due,
            IsFullyPaid = due <= 0,
            PaymentStatus = due < 0 ? "Overpaid" : due == 0 ? "Completed" : paid > 0 ? "PartiallyPaid" : "Pending",
            Payments = orders.SelectMany(order => order.Payments).ToList(),
        };
        return CashierReceiptSnapshot.Sanitize(receipt);
    }
}
