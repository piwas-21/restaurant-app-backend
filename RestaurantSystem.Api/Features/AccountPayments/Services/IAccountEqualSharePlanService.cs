using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountEqualSharePlanService
{
    Task<AccountEqualSharePlanDto> CreateAsync(
        Guid sessionId, CreateAccountEqualSharePlanRequest request, CancellationToken cancellationToken);
}
