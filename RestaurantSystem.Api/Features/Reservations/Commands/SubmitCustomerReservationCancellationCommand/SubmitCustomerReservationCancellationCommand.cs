using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Commands.SubmitCustomerReservationCancellationCommand;

public sealed record SubmitCustomerReservationCancellationCommand(string SessionId, CustomerReservationProofDto Request)
    : ICommand<ApiResponse<CustomerReservationResolutionDto>>;

public sealed class SubmitCustomerReservationCancellationCommandHandler(ICustomerReservationCancellationService service)
    : ICommandHandler<SubmitCustomerReservationCancellationCommand, ApiResponse<CustomerReservationResolutionDto>>
{
    public async Task<ApiResponse<CustomerReservationResolutionDto>> Handle(SubmitCustomerReservationCancellationCommand command,
        CancellationToken cancellationToken) => ApiResponse<CustomerReservationResolutionDto>.SuccessWithData(
            await service.SubmitAsync(command.SessionId, command.Request, cancellationToken));
}
