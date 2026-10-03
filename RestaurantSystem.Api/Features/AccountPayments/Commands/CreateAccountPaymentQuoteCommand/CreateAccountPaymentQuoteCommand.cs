using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.CreateAccountPaymentQuoteCommand;

public sealed record CreateAccountPaymentQuoteCommand(
    Guid ServiceSessionId, CreateAccountPaymentQuoteRequest Request)
    : ICommand<ApiResponse<AccountPaymentOperationDto>>;

public sealed class CreateAccountPaymentQuoteCommandHandler(IAccountPaymentQuoteService service)
    : ICommandHandler<CreateAccountPaymentQuoteCommand, ApiResponse<AccountPaymentOperationDto>>
{
    public async Task<ApiResponse<AccountPaymentOperationDto>> Handle(
        CreateAccountPaymentQuoteCommand command, CancellationToken cancellationToken) =>
        ApiResponse<AccountPaymentOperationDto>.SuccessWithData(await service.CreateQuoteAsync(
            command.ServiceSessionId, command.Request, cancellationToken), "Payment quote created");
}
