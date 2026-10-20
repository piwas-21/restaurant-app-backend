using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

public sealed record CustomerOrderCancellationWriteResult(CustomerOrderCancellationResolutionDto Resolution, bool Changed);

public interface ICustomerOrderCancellationStore
{
    Task<CustomerOrderCancellationPreparationDto> PrepareAsync(CustomerOrderCancellationOwner owner, CancellationToken cancellationToken);
    Task<CustomerOrderCancellationWriteResult> SubmitAsync(CustomerOrderCancellationOwner owner,
        CustomerOrderCancellationIntentDto intent, CancellationToken cancellationToken);
    Task<CustomerOrderCancellationResolutionDto> RecoverAsync(CustomerOrderCancellationOwner owner,
        CustomerOrderCancellationIntentDto intent, CancellationToken cancellationToken);
}
