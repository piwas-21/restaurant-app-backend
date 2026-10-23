using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTableCheckoutService
{
    Task<CustomerTableCheckoutDto> RunAsync(string sessionId, CustomerTablePaymentProofDto proof,
        string? participantToken, CustomerTableCheckoutAction action, CancellationToken cancellationToken);
}
