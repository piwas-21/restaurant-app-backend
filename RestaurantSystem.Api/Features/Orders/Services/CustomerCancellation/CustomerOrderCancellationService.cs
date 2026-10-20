using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

public sealed class CustomerOrderCancellationService(ICustomerOrderCancellationOwners owners,
    ICustomerOrderCancellationStore store, ICustomerOrderCancellationNotifier notifier,
    ILogger<CustomerOrderCancellationService> logger) : ICustomerOrderCancellationService
{
    public Task<CustomerOrderCancellationPreparationDto> PrepareCustomerAsync(Guid orderId, CancellationToken cancellationToken) =>
        store.PrepareAsync(owners.Customer(orderId), cancellationToken);

    public async Task<CustomerOrderCancellationPreparationDto> PrepareOriginalAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken) =>
        await store.PrepareAsync(await owners.OriginalAsync(sessionId, proof, cancellationToken), cancellationToken);

    public Task<CustomerOrderCancellationResolutionDto> SubmitCustomerAsync(CustomerOrderCancellationIntentDto intent,
        CancellationToken cancellationToken) =>
        CompleteAsync(store.SubmitAsync(owners.Customer(intent.OrderId), intent, cancellationToken), cancellationToken);

    public async Task<CustomerOrderCancellationResolutionDto> SubmitOriginalAsync(string sessionId, CustomerOrderCancellationGuestDto request,
        CancellationToken cancellationToken)
    {
        ValidateOriginal(request);
        var owner = await owners.OriginalAsync(sessionId, request.Proof, cancellationToken);
        return await CompleteAsync(store.SubmitAsync(owner, request.Intent, cancellationToken), cancellationToken);
    }

    public Task<CustomerOrderCancellationResolutionDto> RecoverCustomerAsync(CustomerOrderCancellationIntentDto intent,
        CancellationToken cancellationToken) =>
        store.RecoverAsync(owners.Customer(intent.OrderId), intent, cancellationToken);

    public async Task<CustomerOrderCancellationResolutionDto> RecoverOriginalAsync(string sessionId, CustomerOrderCancellationGuestDto request,
        CancellationToken cancellationToken)
    {
        ValidateOriginal(request);
        return await store.RecoverAsync(await owners.OriginalAsync(sessionId, request.Proof, cancellationToken),
            request.Intent, cancellationToken);
    }

    private async Task<CustomerOrderCancellationResolutionDto> CompleteAsync(Task<CustomerOrderCancellationWriteResult> write,
        CancellationToken cancellationToken)
    {
        var result = await write;
        if (result.Changed)
        {
            try { await notifier.NotifyAsync(result.Resolution.OrderId, cancellationToken); }
            catch (Exception exception)
            {
                logger.LogWarning("Cancellation notification unavailable for order {OrderId} ({ExceptionType})",
                    result.Resolution.OrderId, exception.GetType().Name);
            }
        }
        return result.Resolution;
    }

    private static void ValidateOriginal(CustomerOrderCancellationGuestDto request)
    {
        if (request.Proof is null || request.Intent is null)
            throw new BadRequestException("The original checkout proof and cancellation intent are required.");
    }
}
