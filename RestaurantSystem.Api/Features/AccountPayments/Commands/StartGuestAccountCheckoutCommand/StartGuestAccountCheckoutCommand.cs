using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.StartGuestAccountCheckoutCommand;

public sealed record StartGuestAccountCheckoutCommand(Guid ServiceSessionId, Guid OperationId,
    StartAccountCheckoutRequest Request, string? ParticipantCredential, string? ReceiptCredential)
    : ICommand<ApiResponse<AccountCheckoutStartDto>>;

public sealed class StartGuestAccountCheckoutCommandHandler(IAccountCheckoutStartService service)
    : ICommandHandler<StartGuestAccountCheckoutCommand, ApiResponse<AccountCheckoutStartDto>>
{
    public async Task<ApiResponse<AccountCheckoutStartDto>> Handle(StartGuestAccountCheckoutCommand command,
        CancellationToken cancellationToken) => ApiResponse<AccountCheckoutStartDto>.SuccessWithData(
        await service.StartGuestAsync(command.ServiceSessionId, command.OperationId, command.Request.ExpectedVersion,
            command.ParticipantCredential, command.ReceiptCredential, cancellationToken), "Checkout status");
}
