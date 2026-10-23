using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTablePlans
{
    Task<CustomerTablePlanPreparedDto> PrepareAsync(string sessionId, string? participantToken,
        CustomerTablePlanIntentDto request, CancellationToken cancellationToken);
    Task<CustomerTablePlanResolutionDto> SubmitAsync(string sessionId, string? participantToken,
        CustomerTablePlanSubmissionDto request, CancellationToken cancellationToken);
    Task<CustomerTablePlanResolutionDto> ResolveAsync(string sessionId, CustomerTablePlanProofDto proof,
        bool stop, CancellationToken cancellationToken);
}
