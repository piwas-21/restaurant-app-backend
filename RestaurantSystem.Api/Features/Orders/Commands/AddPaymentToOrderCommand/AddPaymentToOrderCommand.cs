using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.AddPaymentToOrderCommand;

public record AddPaymentToOrderCommand : ICommand<ApiResponse<OrderDto>>
{
    public Guid OrderId { get; set; }
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

        if (result.Outcome == OrderPaymentApplicationOutcome.OrderNotInPayableStatus || result.Order == null)
        {
            return ApiResponse<OrderDto>.Failure($"Cannot add payment to {result.NotPayableStatus ?? "Completed"} order");
        }

        // Return the updated order so frontend gets current payment status
        var orderDto = await _mappingService.MapToOrderDtoAsync(result.Order, cancellationToken);
        return ApiResponse<OrderDto>.SuccessWithData(orderDto, "Payment added successfully");
    }
}
