using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTablePaymentProofs
{
    CustomerTablePaymentProofDto Create(CustomerTablePaymentIntentDto request, CustomerTablePaymentOwner owner,
        CustomerTablePaymentBinding binding);
    CustomerTablePaymentBinding Require(CustomerTablePaymentProofDto proof, CustomerTablePaymentOwner owner);
    void RequireInput(CustomerTablePaymentIntentDto request, CustomerTablePaymentOwner owner,
        CustomerTablePaymentBinding binding, CustomerTablePaymentProofDto proof);
}
