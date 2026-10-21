using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Commands;

public sealed record StopCustomerTableRoundCommand(string SessionId, CustomerTableRoundProofDto Proof)
    : ICommand<ApiResponse<CustomerTableRoundResolutionDto>>;

public sealed class StopCustomerTableRoundCommandHandler(ICustomerTableRoundService service)
    : ICommandHandler<StopCustomerTableRoundCommand, ApiResponse<CustomerTableRoundResolutionDto>>
{
    public async Task<ApiResponse<CustomerTableRoundResolutionDto>> Handle(StopCustomerTableRoundCommand command, CancellationToken cancellationToken) =>
        ApiResponse<CustomerTableRoundResolutionDto>.SuccessWithData(
            await service.ResolveAsync(command.SessionId, command.Proof, true, cancellationToken));
}
