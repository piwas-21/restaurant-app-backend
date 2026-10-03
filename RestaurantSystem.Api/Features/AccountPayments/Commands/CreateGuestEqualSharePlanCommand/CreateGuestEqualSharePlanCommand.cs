using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.CreateGuestEqualSharePlanCommand;

public sealed record CreateGuestEqualSharePlanCommand(
    Guid ServiceSessionId,
    string? ParticipantCredential,
    CreateAccountEqualSharePlanRequest Request)
    : ICommand<ApiResponse<AccountEqualSharePlanDto>>;

public sealed class CreateGuestEqualSharePlanCommandHandler(IAccountEqualSharePlanService service)
    : ICommandHandler<CreateGuestEqualSharePlanCommand, ApiResponse<AccountEqualSharePlanDto>>
{
    public async Task<ApiResponse<AccountEqualSharePlanDto>> Handle(
        CreateGuestEqualSharePlanCommand command, CancellationToken cancellationToken) =>
        ApiResponse<AccountEqualSharePlanDto>.SuccessWithData(await service.CreateGuestAsync(
            command.ServiceSessionId, command.ParticipantCredential, command.Request, cancellationToken),
            "Guest equal-share plan created");
}
