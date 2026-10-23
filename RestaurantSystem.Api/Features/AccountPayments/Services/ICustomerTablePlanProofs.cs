using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTablePlanProofs
{
    CustomerTablePlanProofDto Create(CustomerTablePlanIntentDto request, CustomerTablePaymentOwner owner, CustomerTablePlanBinding binding);
    CustomerTablePlanBinding Require(CustomerTablePlanProofDto proof, CustomerTablePaymentOwner owner);
    void RequireInput(CustomerTablePlanIntentDto request, CustomerTablePaymentOwner owner,
        CustomerTablePlanBinding binding, CustomerTablePlanProofDto proof);
}
