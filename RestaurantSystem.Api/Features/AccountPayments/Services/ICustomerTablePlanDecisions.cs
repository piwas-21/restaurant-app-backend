using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface ICustomerTablePlanDecisions
{
    Task RecordAsync(CustomerTablePaymentOwner owner, CustomerTablePlanProofDto proof,
        Guid? planId, bool stop, CancellationToken cancellationToken);
    Task<CustomerTablePlanResolutionDto?> ReadAsync(CustomerTablePaymentOwner owner, CustomerTablePlanProofDto proof,
        CustomerTablePlanBinding binding, CancellationToken cancellationToken);
}
