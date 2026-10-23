using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTableCheckoutProjection
{
    Task<CustomerTableCheckoutDto> ReadAsync(string sessionId, CustomerTablePaymentProofDto proof,
        CustomerTableCheckoutIdentity original, string? participantToken, string? canonicalUrl,
        CancellationToken cancellationToken);
}
