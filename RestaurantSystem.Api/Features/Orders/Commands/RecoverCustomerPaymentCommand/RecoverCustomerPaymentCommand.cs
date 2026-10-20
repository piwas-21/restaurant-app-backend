using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

namespace RestaurantSystem.Api.Features.Orders.Commands.RecoverCustomerPaymentCommand;

public sealed record RecoverCustomerPaymentCommand(string SessionId, CustomerOrderRecoveryDto Proof)
    : ICommand<ApiResponse<CustomerOrderPaymentDto>>;

public sealed class RecoverCustomerPaymentCommandHandler(ICustomerOrderCheckoutPaymentService payments)
    : ICommandHandler<RecoverCustomerPaymentCommand, ApiResponse<CustomerOrderPaymentDto>>
{
    public async Task<ApiResponse<CustomerOrderPaymentDto>> Handle(RecoverCustomerPaymentCommand command,
        CancellationToken cancellationToken) => ApiResponse<CustomerOrderPaymentDto>.SuccessWithData(
            await payments.RecoverAsync(command.SessionId, command.Proof, cancellationToken));
}
