using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

public interface ICustomerOrderCancellationService
{
    Task<CustomerOrderCancellationPreparationDto> PrepareCustomerAsync(Guid orderId, CancellationToken cancellationToken);
    Task<CustomerOrderCancellationPreparationDto> PrepareOriginalAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken);
    Task<CustomerOrderCancellationResolutionDto> SubmitCustomerAsync(CustomerOrderCancellationIntentDto intent,
        CancellationToken cancellationToken);
    Task<CustomerOrderCancellationResolutionDto> SubmitOriginalAsync(string sessionId, CustomerOrderCancellationGuestDto request,
        CancellationToken cancellationToken);
    Task<CustomerOrderCancellationResolutionDto> RecoverCustomerAsync(CustomerOrderCancellationIntentDto intent,
        CancellationToken cancellationToken);
    Task<CustomerOrderCancellationResolutionDto> RecoverOriginalAsync(string sessionId, CustomerOrderCancellationGuestDto request,
        CancellationToken cancellationToken);
}
