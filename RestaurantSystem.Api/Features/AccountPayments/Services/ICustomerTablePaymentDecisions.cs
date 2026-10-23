using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTablePaymentDecisions
{
    Task<CustomerTablePaymentResolutionDto?> ReadAsync(CustomerTablePaymentOwner owner,
        CustomerTablePaymentProofDto proof, CustomerTablePaymentBinding binding, CancellationToken cancellationToken);
}
