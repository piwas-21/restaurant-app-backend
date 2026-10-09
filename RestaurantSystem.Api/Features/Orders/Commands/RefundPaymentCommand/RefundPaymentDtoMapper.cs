using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Commands.RefundPaymentCommand;

internal static class RefundPaymentDtoMapper
{
    public static OrderPaymentDto Map(OrderPayment payment) => new()
    {
        Id = payment.Id,
        OrderId = payment.OrderId,
        PaymentMethod = payment.PaymentMethod.ToString(),
        Amount = payment.Amount,
        TipMinor = payment.TipMinor,
        Status = payment.Status.ToString(),
        TransactionId = payment.TransactionId,
        ReferenceNumber = payment.ReferenceNumber,
        PaymentDate = payment.PaymentDate,
        CardLastFourDigits = payment.CardLastFourDigits,
        CardType = payment.CardType,
        PaymentGateway = payment.PaymentGateway,
        PaymentNotes = payment.PaymentNotes,
        IsRefunded = payment.IsRefunded,
        RefundedAmount = payment.RefundedAmount,
        RefundedTipMinor = payment.RefundedTipMinor,
        RefundDate = payment.RefundDate,
        RefundReason = payment.RefundReason
    };
}
