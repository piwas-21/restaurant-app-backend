using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Commands.PrepareCustomerReservationCommand;

public sealed record PrepareCustomerReservationCommand(string SessionId, CustomerReservationCreateDto Request)
    : ICommand<ApiResponse<CustomerReservationProofDto>>;

public sealed class PrepareCustomerReservationCommandHandler(ICustomerReservationOperationGuard guard)
    : ICommandHandler<PrepareCustomerReservationCommand, ApiResponse<CustomerReservationProofDto>>
{
    public async Task<ApiResponse<CustomerReservationProofDto>> Handle(PrepareCustomerReservationCommand command,
        CancellationToken cancellationToken) => ApiResponse<CustomerReservationProofDto>.SuccessWithData(
            await guard.PrepareAsync(command.SessionId, command.Request, cancellationToken));
}
