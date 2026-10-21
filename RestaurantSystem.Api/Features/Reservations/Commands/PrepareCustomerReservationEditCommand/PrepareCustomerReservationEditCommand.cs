using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Commands.PrepareCustomerReservationEditCommand;

public sealed record PrepareCustomerReservationEditCommand(string SessionId, CustomerReservationEditDto Request) : ICommand<ApiResponse<CustomerReservationEditPreparedDto>>;
public sealed class PrepareCustomerReservationEditCommandHandler(ICustomerReservationEditService service)
    : ICommandHandler<PrepareCustomerReservationEditCommand, ApiResponse<CustomerReservationEditPreparedDto>>
{
    public async Task<ApiResponse<CustomerReservationEditPreparedDto>> Handle(PrepareCustomerReservationEditCommand command, CancellationToken cancellationToken) =>
        ApiResponse<CustomerReservationEditPreparedDto>.SuccessWithData(await service.PrepareAsync(command.SessionId, command.Request, cancellationToken));
}
