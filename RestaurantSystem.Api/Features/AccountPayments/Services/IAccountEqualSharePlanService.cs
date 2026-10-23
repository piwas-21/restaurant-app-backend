using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountEqualSharePlanService
{
    Task<AccountSharePlanReviewDto> ReviewGuestAsync(Guid sessionId, string? participantCredential,
        CreateAccountEqualSharePlanRequest request, CancellationToken cancellationToken);

    Task<AccountEqualSharePlanDto> CreateAsync(
        Guid sessionId, CreateAccountEqualSharePlanRequest request, CancellationToken cancellationToken);

    Task<AccountEqualSharePlanDto> CreateGuestAsync(
        Guid sessionId, string? participantCredential, CreateAccountEqualSharePlanRequest request,
        CancellationToken cancellationToken);
}
