using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.CancelGuestAccountCheckoutCommand;

public sealed record CancelGuestAccountCheckoutCommand(Guid ServiceSessionId, Guid OperationId,
    StartAccountCheckoutRequest Request, string? ParticipantCredential)
    : ICommand<ApiResponse<AccountCheckoutStartDto>>;

public sealed class CancelGuestAccountCheckoutCommandHandler(IAccountCheckoutCancelService service)
    : ICommandHandler<CancelGuestAccountCheckoutCommand, ApiResponse<AccountCheckoutStartDto>>
{
    public async Task<ApiResponse<AccountCheckoutStartDto>> Handle(CancelGuestAccountCheckoutCommand command,
        CancellationToken cancellationToken) => ApiResponse<AccountCheckoutStartDto>.SuccessWithData(
        await service.RequestGuestAsync(command.ServiceSessionId, command.OperationId,
            command.Request.ExpectedVersion, command.ParticipantCredential, cancellationToken), "Checkout status");
}
