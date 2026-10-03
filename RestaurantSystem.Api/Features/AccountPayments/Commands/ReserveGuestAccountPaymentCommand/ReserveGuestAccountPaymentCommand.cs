using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.ReserveGuestAccountPaymentCommand;

public sealed record ReserveGuestAccountPaymentCommand(
    Guid ServiceSessionId,
    Guid OperationId,
    string? ParticipantCredential,
    ReserveAccountPaymentRequest Request)
    : ICommand<ApiResponse<AccountPaymentOperationDto>>;

public sealed class ReserveGuestAccountPaymentCommandHandler(IAccountPaymentReservationService service)
    : ICommandHandler<ReserveGuestAccountPaymentCommand, ApiResponse<AccountPaymentOperationDto>>
{
    public async Task<ApiResponse<AccountPaymentOperationDto>> Handle(
        ReserveGuestAccountPaymentCommand command, CancellationToken cancellationToken) =>
        ApiResponse<AccountPaymentOperationDto>.SuccessWithData(await service.ReserveGuestAsync(
            command.ServiceSessionId, command.OperationId, command.ParticipantCredential,
            command.Request, cancellationToken), "Guest payment reserved");
}
