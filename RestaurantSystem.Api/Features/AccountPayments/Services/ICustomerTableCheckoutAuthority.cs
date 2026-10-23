using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTableCheckoutAuthority
{
    Task RequireStartAsync(CustomerTablePaymentOwner owner, CustomerTablePaymentProofDto proof,
        CustomerTablePaymentBinding binding, string? participantToken, CancellationToken cancellationToken);
    Task RecheckAsync(CustomerTablePaymentOwner owner, CustomerTablePaymentProofDto proof,
        CustomerTablePaymentBinding binding, string? participantToken, CancellationToken cancellationToken);
    Task<bool> MayOpenAsync(CustomerTableCheckoutIdentity original, CustomerTablePaymentProofDto proof,
        string? participantToken, CancellationToken cancellationToken);
}
