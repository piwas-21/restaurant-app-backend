using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerCheckoutAcceptance
{
    Task<CustomerCheckoutPreparationDto> PrepareAsync(string sessionId, CustomerCheckoutIntentDto intent,
        CancellationToken cancellationToken);
    Task<CustomerOrderResolutionDto> SubmitAsync(string sessionId, CustomerCheckoutSubmissionDto submission,
        CancellationToken cancellationToken);
    Task<CustomerOrderResolutionDto> RecoverAsync(string sessionId, CustomerOrderRecoveryDto recovery,
        CancellationToken cancellationToken);
    Task<CustomerOrderResolutionDto> CancelAsync(string sessionId, CustomerOrderRecoveryDto recovery,
        CancellationToken cancellationToken);
}
