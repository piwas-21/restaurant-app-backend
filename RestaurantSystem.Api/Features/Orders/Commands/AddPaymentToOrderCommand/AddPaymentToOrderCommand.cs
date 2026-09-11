using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.AddPaymentToOrderCommand;

public record AddPaymentToOrderCommand : ICommand<ApiResponse<OrderDto>>
{
    public Guid OrderId { get; set; }

    // Client operation key for idempotent tender recording (#523): the POS mints one Guid per
    // cashier action and sends the SAME one on retry, so a network timeout after commit is
    // answered with the original result instead of a second tender. Required: without it the
    // endpoint cannot tell a retry from a new payment. A repeat with a different method or
    // amount is refused with ErrorCodes.PaymentOperationPayloadMismatch.
    // Required on the wire too (S6964): an omitted operation id cannot silently become a
    // brand-new payment instead of the retry the till intended.
    [JsonRequired]
    public Guid OperationId { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public string? TransactionId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? CardLastFourDigits { get; set; }
    public string? CardType { get; set; }
    // `PaymentGateway` used to live here, copied verbatim into the ledger. Removed in S11 — see
    // TenderCustody for why a caller-settable gateway name locks a till payment out of refunds.
    public string? PaymentNotes { get; set; }
}

public class AddPaymentToOrderCommandHandler : ICommandHandler<AddPaymentToOrderCommand, ApiResponse<OrderDto>>
{
    private readonly IOrderPaymentApplicator _paymentApplicator;
    private readonly IOrderMappingService _mappingService;

    public AddPaymentToOrderCommandHandler(
        IOrderPaymentApplicator paymentApplicator,
        IOrderMappingService mappingService)
    {
        _paymentApplicator = paymentApplicator;
        _mappingService = mappingService;
    }

    public async Task<ApiResponse<OrderDto>> Handle(AddPaymentToOrderCommand command, CancellationToken cancellationToken)
    {
        var result = await _paymentApplicator.ApplyToOrderAsync(
            command.OrderId,
            new OrderPaymentTender
            {
                OperationId = command.OperationId,
                PaymentMethod = command.PaymentMethod,
                Amount = command.Amount,
                TransactionId = command.TransactionId,
                ReferenceNumber = command.ReferenceNumber,
                CardLastFourDigits = command.CardLastFourDigits,
                CardType = command.CardType,
                PaymentNotes = command.PaymentNotes,
            },
            cancellationToken);

        if (result.Outcome == OrderPaymentApplicationOutcome.OrderNotFound)
        {
            return ApiResponse<OrderDto>.Failure("Order not found");
        }

        if (result.Outcome == OrderPaymentApplicationOutcome.OperationPayloadMismatch)
        {
            // A retried operation may not change its own payload. Distinct machine-readable
            // refusal: the frontend branches on the code instead of substring-matching the
            // message (ErrorCodes is the stable contract, wording is not).
            return ApiResponse<OrderDto>.FailureWithCode(
                "This payment was already submitted with a different amount or payment method. "
                + "The original payment stands — refresh the order before continuing.",
                ErrorCodes.PaymentOperationPayloadMismatch);
        }

        if (result.Outcome == OrderPaymentApplicationOutcome.OperationIdReused)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "This operation was already used for a different order. "
                + "Start the payment again so a new operation is generated.",
                ErrorCodes.PaymentOperationIdReused);
        }

        if (result.Outcome == OrderPaymentApplicationOutcome.OrderNotInPayableStatus || result.Order == null)
        {
            return ApiResponse<OrderDto>.Failure($"Cannot add payment to {result.NotPayableStatus ?? "Completed"} order");
        }

        // Return the updated order so frontend gets current payment status. A replay says so,
        // so the UI can tell the cashier "already recorded" from a fresh success without
        // guessing from row counts.
        var orderDto = await _mappingService.MapToOrderDtoAsync(result.Order, cancellationToken);
        return result.IsIdempotentReplay
            ? ApiResponse<OrderDto>.SuccessWithData(orderDto, "Payment already recorded")
            : ApiResponse<OrderDto>.SuccessWithData(orderDto, "Payment added successfully");
    }
}
