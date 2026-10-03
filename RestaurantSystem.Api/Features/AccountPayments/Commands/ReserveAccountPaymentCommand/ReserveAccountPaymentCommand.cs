using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.ReserveAccountPaymentCommand;

public sealed record ReserveAccountPaymentCommand(
    Guid ServiceSessionId, Guid OperationId, ReserveAccountPaymentRequest Request)
    : ICommand<ApiResponse<AccountPaymentOperationDto>>;

public sealed class ReserveAccountPaymentCommandHandler(IAccountPaymentReservationService service)
    : ICommandHandler<ReserveAccountPaymentCommand, ApiResponse<AccountPaymentOperationDto>>
{
    public async Task<ApiResponse<AccountPaymentOperationDto>> Handle(
        ReserveAccountPaymentCommand command, CancellationToken cancellationToken) =>
        ApiResponse<AccountPaymentOperationDto>.SuccessWithData(await service.ReserveAsync(
            command.ServiceSessionId, command.OperationId, command.Request, cancellationToken), "Payment reserved");
}
