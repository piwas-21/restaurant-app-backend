using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CreateCustomerReservationCommand;

public sealed record CreateCustomerReservationCommand(string SessionId, CustomerReservationCreateDto Request) : ICommand<ApiResponse<CustomerReservationResolutionDto>>;

public sealed class CreateCustomerReservationCommandHandler(ICustomerReservationOperationService service)
    : ICommandHandler<CreateCustomerReservationCommand, ApiResponse<CustomerReservationResolutionDto>>
{
    public async Task<ApiResponse<CustomerReservationResolutionDto>> Handle(CreateCustomerReservationCommand command,
        CancellationToken cancellationToken) => ApiResponse<CustomerReservationResolutionDto>.SuccessWithData(
            await service.CreateAsync(command.SessionId, command.Request, cancellationToken));
}
