using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Commands.ResolveCustomerReservationCommand;

public sealed record ResolveCustomerReservationCommand(string SessionId, CustomerReservationProofDto Proof,
    bool CancelAttempt) : ICommand<ApiResponse<CustomerReservationResolutionDto>>;

public sealed class ResolveCustomerReservationCommandHandler(ICustomerReservationOperationService service)
    : ICommandHandler<ResolveCustomerReservationCommand, ApiResponse<CustomerReservationResolutionDto>>
{
    public async Task<ApiResponse<CustomerReservationResolutionDto>> Handle(ResolveCustomerReservationCommand command,
        CancellationToken cancellationToken) => ApiResponse<CustomerReservationResolutionDto>.SuccessWithData(
            await service.ResolveAsync(command.SessionId, command.Proof, command.CancelAttempt, cancellationToken));
}
