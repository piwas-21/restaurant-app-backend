using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerCheckoutPreparation(ICustomerCheckoutGuard guard, IBasketWriteCoordinator coordinator,
    ICustomerCheckoutDraft draft, ICustomerCheckoutIntentPolicy policy) : ICustomerCheckoutPreparation
{
    public Task<CustomerCheckoutPreparationDto> PrepareAsync(string sessionId, CustomerCheckoutIntentDto intent,
        CancellationToken cancellationToken)
    {
        var owner = guard.EnsureOwner(sessionId);
        return coordinator.RunAsync(sessionId, owner, async () =>
        {
            var review = await ReviewUnderLockAsync(sessionId, intent, cancellationToken);
            return new CustomerCheckoutPreparationDto(1, CustomerCheckoutIntentHash.Compute(intent), review.Quote);
        }, cancellationToken);
    }

    public async Task<CustomerCheckoutAcceptanceReview> ReviewUnderLockAsync(string sessionId,
        CustomerCheckoutIntentDto intent, CancellationToken cancellationToken)
    {
        var owner = guard.EnsureOwner(sessionId);
        var context = await guard.ReviewAsync(sessionId, owner, intent.Review, cancellationToken);
        var prepared = await draft.PrepareAsync(context, intent.Review, cancellationToken);
        var quote = CustomerCheckoutProjection.Create(context, intent.Review, prepared.Order);
        await policy.EnsureAsync(intent, quote, cancellationToken);
        return new(context, prepared, quote);
    }
}
