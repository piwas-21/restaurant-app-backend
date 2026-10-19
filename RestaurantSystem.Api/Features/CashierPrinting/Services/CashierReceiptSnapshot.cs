using System.Text.Json;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.CashierPrinting.Services;

/// <summary>Cashier-only snapshot: retains financial/component identities, excludes guest contact and free text.</summary>
internal static class CashierReceiptSnapshot
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static OrderDto FromBill(TableBillDto bill)
    {
        var orders = bill.Orders.Where(order => order.Status != "Cancelled").ToList();
        var remaining = bill.Rounds.Where(round => round.SettlementState is not ("Refunded" or "PartiallyRefunded"))
            .Sum(round => round.Outstanding);
        var refunded = bill.Rounds.Sum(round => round.RefundedAmount);
        var receipt = new OrderDto
        {
            Id = bill.ServiceSessionId!.Value,
            ServiceSessionId = bill.ServiceSessionId,
            OrderNumber = bill.TableLabel ?? bill.TableNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ?? bill.ServiceSessionId.Value.ToString(),
            Type = "DineIn",
            TableId = bill.TableId,
            TableLabel = bill.TableLabel,
            TableNumber = bill.TableNumber,
            Currency = bill.Currency,
            OrderDate = bill.GeneratedAt,
            SubTotal = bill.SubTotal,
            Tax = bill.Tax,
            Discount = bill.Discount,
            CustomerDiscountAmount = orders.Sum(order => order.CustomerDiscountAmount),
            DeliveryFee = orders.Sum(order => order.DeliveryFee),
            Tip = bill.Tip,
            Total = bill.Total,
            PayableTotal = bill.Total,
            TotalPaid = bill.TotalPaid,
            RemainingAmount = remaining,
            IsFullyPaid = remaining <= 0 && refunded == 0,
            CashierBillRefundedAmount = refunded,
            CashierBillCredit = bill.Credit,
            IsTableBill = true,
            PaymentTipMinor = orders.Sum(order => order.PaymentTipMinor),
            BillChargeAmount = orders.Sum(order => order.BillChargeAmount),
            BillCharges = orders.SelectMany(order => order.BillCharges).ToList(),
            BillingCreditAmount = bill.BillingCreditAmount,
            PaymentStatus = refunded > 0 && bill.Rounds.All(round => round.SettlementState == "Refunded") ? "Refunded"
                : refunded > 0 ? "PartiallyRefunded"
                : bill.Credit > 0 ? "Overpaid" : remaining <= 0 ? "Paid" : "Pending",
            Items = orders.SelectMany(order => order.Items).ToList(),
            Payments = orders.SelectMany(order => order.Payments).ToList(),
        };
        return Sanitize(receipt);
    }

    internal static OrderDto Sanitize(OrderDto source)
    {
        // Clone before redaction: these same DTOs may also back the interactive staff response.
        var receipt = JsonSerializer.Deserialize<OrderDto>(JsonSerializer.Serialize(source, Json), Json)!;
        receipt.GuestStatusToken = null; receipt.UserId = null; receipt.CustomerName = null;
        receipt.CustomerEmail = null; receipt.CustomerPhone = null; receipt.DeliveryAddress = null;
        receipt.Notes = null; receipt.CancellationReason = null; receipt.FocusReason = null;
        receipt.StatusHistory = []; receipt.PermittedActions = null;
        receipt.RoutingStates = [];
        receipt.Payments = receipt.Payments.Select(payment => payment with
        {
            PaymentNotes = null,
            ReferenceNumber = null,
            RefundReason = null,
            TransactionId = null,
        }).ToList();
        if (!receipt.IsTableBill)
            receipt.CashierBillRefundedAmount = receipt.Payments.Sum(payment => payment.RefundedAmount
                ?? (payment.IsRefunded ? payment.Amount : 0));
        foreach (var item in receipt.Items) SanitizeItem(item);
        return receipt;
    }

    private static void SanitizeItem(OrderItemDto item)
    {
        item.SpecialInstructions = null; item.IngredientCustomizations = [];
        foreach (var child in item.SideItems ?? []) SanitizeItem(child);
    }
}
