using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands;

public sealed record SubmitCustomerTablePlanCommand(string SessionId, string? ParticipantToken, CustomerTablePlanSubmissionDto Request) : ICommand<ApiResponse<CustomerTablePlanResolutionDto>>;

public sealed class SubmitCustomerTablePlanCommandHandler(ICustomerTablePlans plans) : ICommandHandler<SubmitCustomerTablePlanCommand, ApiResponse<CustomerTablePlanResolutionDto>>
{
    public async Task<ApiResponse<CustomerTablePlanResolutionDto>> Handle(SubmitCustomerTablePlanCommand query, CancellationToken cancellationToken) =>
        ApiResponse<CustomerTablePlanResolutionDto>.SuccessWithData(await plans.SubmitAsync(query.SessionId, query.ParticipantToken, query.Request, cancellationToken));
}
