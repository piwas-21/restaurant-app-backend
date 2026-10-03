using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.ReleaseGuestAccountPaymentCommand;

public sealed record ReleaseGuestAccountPaymentCommand(
    Guid ServiceSessionId,
    Guid OperationId,
    string? ParticipantCredential,
    ReleaseAccountPaymentRequest Request)
    : ICommand<ApiResponse<AccountPaymentOperationDto>>;

public sealed class ReleaseGuestAccountPaymentCommandHandler(IAccountPaymentReservationService service)
    : ICommandHandler<ReleaseGuestAccountPaymentCommand, ApiResponse<AccountPaymentOperationDto>>
{
    public async Task<ApiResponse<AccountPaymentOperationDto>> Handle(
        ReleaseGuestAccountPaymentCommand command, CancellationToken cancellationToken) =>
        ApiResponse<AccountPaymentOperationDto>.SuccessWithData(await service.ReleaseGuestAsync(
            command.ServiceSessionId, command.OperationId, command.ParticipantCredential,
            command.Request, cancellationToken), "Guest payment released");
}
