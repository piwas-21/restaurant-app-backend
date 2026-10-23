using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTablePaymentAcceptance
{
    Task<Guid> AcceptAsync(CustomerTablePaymentOwner owner, CustomerTablePaymentProofDto proof,
        CustomerTablePaymentBinding binding, CustomerTablePaymentIntentDto intent, string? participantToken,
        CancellationToken cancellationToken);
}
