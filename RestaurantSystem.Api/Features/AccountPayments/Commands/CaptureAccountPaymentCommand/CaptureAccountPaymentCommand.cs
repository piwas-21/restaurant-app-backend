using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.CaptureAccountPaymentCommand;

public sealed record CaptureAccountPaymentCommand(Guid ServiceSessionId, Guid OperationId,
    CaptureAccountPaymentRequest Request) : ICommand<ApiResponse<AccountPaymentOperationDto>>;

public sealed class CaptureAccountPaymentCommandHandler(IAccountPaymentCaptureService service)
    : ICommandHandler<CaptureAccountPaymentCommand, ApiResponse<AccountPaymentOperationDto>>
{
    public async Task<ApiResponse<AccountPaymentOperationDto>> Handle(CaptureAccountPaymentCommand command,
        CancellationToken cancellationToken) => ApiResponse<AccountPaymentOperationDto>.SuccessWithData(
            await service.CaptureManualAsync(command.ServiceSessionId, command.OperationId, command.Request,
                cancellationToken), "Account contribution collected");
}
