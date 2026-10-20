using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Commands.ResolveCustomerReservationEditCommand;

public sealed record ResolveCustomerReservationEditCommand(string SessionId, CustomerReservationProofDto Proof, bool CancelAttempt) : ICommand<ApiResponse<CustomerReservationResolutionDto>>;
public sealed class ResolveCustomerReservationEditCommandHandler(ICustomerReservationEditService service)
    : ICommandHandler<ResolveCustomerReservationEditCommand, ApiResponse<CustomerReservationResolutionDto>>
{
    public async Task<ApiResponse<CustomerReservationResolutionDto>> Handle(ResolveCustomerReservationEditCommand command, CancellationToken cancellationToken) =>
        ApiResponse<CustomerReservationResolutionDto>.SuccessWithData(await service.ResolveAsync(command.SessionId, command.Proof, command.CancelAttempt, cancellationToken));
}
