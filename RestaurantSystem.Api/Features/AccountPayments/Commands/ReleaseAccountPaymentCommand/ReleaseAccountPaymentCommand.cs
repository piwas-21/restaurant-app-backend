using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.ReleaseAccountPaymentCommand;

public sealed record ReleaseAccountPaymentCommand(
    Guid ServiceSessionId, Guid OperationId, ReleaseAccountPaymentRequest Request)
    : ICommand<ApiResponse<AccountPaymentOperationDto>>;

public sealed class ReleaseAccountPaymentCommandHandler(IAccountPaymentReservationService service)
    : ICommandHandler<ReleaseAccountPaymentCommand, ApiResponse<AccountPaymentOperationDto>>
{
    public async Task<ApiResponse<AccountPaymentOperationDto>> Handle(
        ReleaseAccountPaymentCommand command, CancellationToken cancellationToken) =>
        ApiResponse<AccountPaymentOperationDto>.SuccessWithData(await service.ReleaseAsync(
            command.ServiceSessionId, command.OperationId, command.Request, cancellationToken), "Payment released");
}
