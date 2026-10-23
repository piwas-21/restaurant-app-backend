using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands;

public sealed record StopCustomerTablePlanCommand(string SessionId, CustomerTablePlanProofDto Proof) : ICommand<ApiResponse<CustomerTablePlanResolutionDto>>;

public sealed class StopCustomerTablePlanCommandHandler(ICustomerTablePlans plans) : ICommandHandler<StopCustomerTablePlanCommand, ApiResponse<CustomerTablePlanResolutionDto>>
{
    public async Task<ApiResponse<CustomerTablePlanResolutionDto>> Handle(StopCustomerTablePlanCommand query, CancellationToken cancellationToken) =>
        ApiResponse<CustomerTablePlanResolutionDto>.SuccessWithData(await plans.ResolveAsync(query.SessionId, query.Proof, true, cancellationToken));
}
