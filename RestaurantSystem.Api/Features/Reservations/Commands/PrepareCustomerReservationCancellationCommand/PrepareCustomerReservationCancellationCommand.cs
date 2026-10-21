using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Commands.PrepareCustomerReservationCancellationCommand;

public sealed record PrepareCustomerReservationCancellationCommand(string SessionId, CustomerReservationCancellationDto Request)
    : ICommand<ApiResponse<CustomerReservationCancellationPreparedDto>>;

public sealed class PrepareCustomerReservationCancellationCommandHandler(ICustomerReservationCancellationService service)
    : ICommandHandler<PrepareCustomerReservationCancellationCommand, ApiResponse<CustomerReservationCancellationPreparedDto>>
{
    public async Task<ApiResponse<CustomerReservationCancellationPreparedDto>> Handle(PrepareCustomerReservationCancellationCommand command,
        CancellationToken cancellationToken) => ApiResponse<CustomerReservationCancellationPreparedDto>.SuccessWithData(
            await service.PrepareAsync(command.SessionId, command.Request, cancellationToken));
}
