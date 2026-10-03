using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.CreateGuestAccountPaymentQuoteCommand;

public sealed record CreateGuestAccountPaymentQuoteCommand(
    Guid ServiceSessionId,
    string? ParticipantCredential,
    CreateAccountPaymentQuoteRequest Request)
    : ICommand<ApiResponse<AccountPaymentOperationDto>>;

public sealed class CreateGuestAccountPaymentQuoteCommandHandler(IAccountPaymentQuoteService service)
    : ICommandHandler<CreateGuestAccountPaymentQuoteCommand, ApiResponse<AccountPaymentOperationDto>>
{
    public async Task<ApiResponse<AccountPaymentOperationDto>> Handle(
        CreateGuestAccountPaymentQuoteCommand command, CancellationToken cancellationToken) =>
        ApiResponse<AccountPaymentOperationDto>.SuccessWithData(await service.CreateGuestQuoteAsync(
            command.ServiceSessionId, command.ParticipantCredential, command.Request, cancellationToken),
            "Guest payment quote created");
}
