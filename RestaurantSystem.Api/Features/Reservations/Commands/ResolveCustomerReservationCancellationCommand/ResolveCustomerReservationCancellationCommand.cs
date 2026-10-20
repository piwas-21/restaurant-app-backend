using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Commands.ResolveCustomerReservationCancellationCommand;

public sealed record ResolveCustomerReservationCancellationCommand(string SessionId, CustomerReservationProofDto Request, bool CancelAttempt)
    : ICommand<ApiResponse<CustomerReservationResolutionDto>>;

public sealed class ResolveCustomerReservationCancellationCommandHandler(ICustomerReservationCancellationService service)
    : ICommandHandler<ResolveCustomerReservationCancellationCommand, ApiResponse<CustomerReservationResolutionDto>>
{
    public async Task<ApiResponse<CustomerReservationResolutionDto>> Handle(ResolveCustomerReservationCancellationCommand command,
        CancellationToken cancellationToken) => ApiResponse<CustomerReservationResolutionDto>.SuccessWithData(
            await service.ResolveAsync(command.SessionId, command.Request, command.CancelAttempt, cancellationToken));
}
