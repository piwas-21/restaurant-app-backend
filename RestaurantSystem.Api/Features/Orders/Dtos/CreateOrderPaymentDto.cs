using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>
/// The tender a caller declares when creating an order — an <em>intent</em> to pay,
/// never a record of money received.
/// </summary>
/// <remarks>
/// Deliberately carries no gateway metadata. <c>TransactionId</c>, <c>ReferenceNumber</c>,
/// <c>CardLastFourDigits</c>, <c>CardType</c> and <c>PaymentGateway</c> used to live here and
/// were written to the ledger verbatim from the request body — on <c>POST /api/Orders</c>,
/// which is anonymous. That let any caller fabricate a payment reference for a payment that
/// never happened. Those fields belong to <c>AddPaymentToOrderCommand</c> on the staff-only
/// <c>POST /api/Orders/{id}/payments</c> path, which runs after someone has actually seen the
/// money. Do not reintroduce them here.
/// </remarks>
public record CreateOrderPaymentDto
{
    public PaymentMethod PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentNotes { get; set; }
}
