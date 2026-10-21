using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Commands;

public sealed record SubmitCustomerTableRoundCommand(string SessionId, string? ParticipantToken, CustomerTableRoundIntentDto Request)
    : ICommand<ApiResponse<CustomerTableRoundResolutionDto>>;

public sealed class SubmitCustomerTableRoundCommandHandler(ICustomerTableRoundService service)
    : ICommandHandler<SubmitCustomerTableRoundCommand, ApiResponse<CustomerTableRoundResolutionDto>>
{
    public async Task<ApiResponse<CustomerTableRoundResolutionDto>> Handle(SubmitCustomerTableRoundCommand command, CancellationToken cancellationToken) =>
        ApiResponse<CustomerTableRoundResolutionDto>.SuccessWithData(
            await service.SubmitAsync(command.SessionId, command.ParticipantToken, command.Request, cancellationToken));
}
