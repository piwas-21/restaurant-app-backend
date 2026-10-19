using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerCheckoutQuoteService(ICustomerCheckoutGuard guard, IBasketWriteCoordinator coordinator,
    ICustomerCheckoutDraft draft) : ICustomerCheckoutQuoteService
{
    public Task<CustomerCheckoutQuoteDto> QuoteAsync(string sessionId, CustomerCheckoutReviewDto review,
        CancellationToken cancellationToken)
    {
        var userId = guard.EnsureOwner(sessionId);
        return coordinator.RunAsync(sessionId, userId, async () =>
        {
            var context = await guard.ReviewAsync(sessionId, userId, review, cancellationToken);
            var order = await draft.BuildAsync(context, review, cancellationToken);
            return CustomerCheckoutProjection.Create(context, review, order);
        }, cancellationToken);
    }
}
