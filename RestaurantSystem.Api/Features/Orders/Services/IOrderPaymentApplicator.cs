using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Where an apply-payment attempt stopped. <see cref="PaymentApplicationResult.Order"/>
/// is populated only on <see cref="Applied"/>.</summary>
public enum OrderPaymentApplicationOutcome
{
    Applied,
    OrderNotFound,
    OrderNotInPayableStatus,
}

public record OrderPaymentTender
{
    public PaymentMethod PaymentMethod { get; init; }
    public decimal Amount { get; init; }
    public string? TransactionId { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? CardLastFourDigits { get; init; }
    public string? CardType { get; init; }
    public string? PaymentNotes { get; init; }
}

public record PaymentApplicationResult(
    OrderPaymentApplicationOutcome Outcome,
    Order? Order,
    string? NotPayableStatus = null)
{
    public static PaymentApplicationResult Applied(Order order) => new(OrderPaymentApplicationOutcome.Applied, order);
    public static PaymentApplicationResult Failed(OrderPaymentApplicationOutcome outcome) => new(outcome, null);

    /// <summary>The order exists but is Cancelled/Completed — carries that status for the user-facing message.</summary>
    public static PaymentApplicationResult NotPayable(string status) =>
        new(OrderPaymentApplicationOutcome.OrderNotInPayableStatus, null, status);
}

/// <summary>
/// The one implementation of "take a tender for an order": replace Pending
/// placeholders, record the payment as captured, and recompute the order's
/// TotalPaid / RemainingAmount / PaymentStatus. Serves the single-order till
/// flow (<c>AddPaymentToOrderCommand</c>) and the table-bill flow
/// (<c>AddTableBillPaymentCommand</c>) so the two cannot drift — #286 showed
/// what payment handlers disagreeing looks like.
/// </summary>
public interface IOrderPaymentApplicator
{
    Task<PaymentApplicationResult> ApplyToOrderAsync(Guid orderId, OrderPaymentTender tender, CancellationToken cancellationToken);
}
