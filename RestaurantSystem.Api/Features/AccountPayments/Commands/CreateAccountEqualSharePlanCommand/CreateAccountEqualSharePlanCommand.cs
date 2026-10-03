using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.CreateAccountEqualSharePlanCommand;

public sealed record CreateAccountEqualSharePlanCommand(
    Guid ServiceSessionId, CreateAccountEqualSharePlanRequest Request)
    : ICommand<ApiResponse<AccountEqualSharePlanDto>>;

public sealed class CreateAccountEqualSharePlanCommandHandler(IAccountEqualSharePlanService service)
    : ICommandHandler<CreateAccountEqualSharePlanCommand, ApiResponse<AccountEqualSharePlanDto>>
{
    public async Task<ApiResponse<AccountEqualSharePlanDto>> Handle(
        CreateAccountEqualSharePlanCommand command, CancellationToken cancellationToken) =>
        ApiResponse<AccountEqualSharePlanDto>.SuccessWithData(await service.CreateAsync(
            command.ServiceSessionId, command.Request, cancellationToken), "Equal-share plan created");
}
