using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTablePaymentOperations
{
    Task<CustomerTablePaymentResolutionDto> SubmitAsync(string sessionId, string? participantToken,
        CustomerTablePaymentSubmissionDto request, CancellationToken cancellationToken);
    Task<CustomerTablePaymentResolutionDto> ResolveAsync(string sessionId, CustomerTablePaymentProofDto proof,
        bool stop, CancellationToken cancellationToken);
}
