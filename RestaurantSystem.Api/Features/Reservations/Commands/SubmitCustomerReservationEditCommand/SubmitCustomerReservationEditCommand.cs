using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Commands.SubmitCustomerReservationEditCommand;

public sealed record SubmitCustomerReservationEditCommand(string SessionId, CustomerReservationEditDto Request) : ICommand<ApiResponse<CustomerReservationResolutionDto>>;
public sealed class SubmitCustomerReservationEditCommandHandler(ICustomerReservationEditService service)
    : ICommandHandler<SubmitCustomerReservationEditCommand, ApiResponse<CustomerReservationResolutionDto>>
{
    public async Task<ApiResponse<CustomerReservationResolutionDto>> Handle(SubmitCustomerReservationEditCommand command, CancellationToken cancellationToken) =>
        ApiResponse<CustomerReservationResolutionDto>.SuccessWithData(await service.SubmitAsync(command.SessionId, command.Request, cancellationToken));
}
