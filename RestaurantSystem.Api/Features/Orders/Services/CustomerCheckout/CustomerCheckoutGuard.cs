using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Payments.Queries.GetOnlinePaymentAvailabilityQuery;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerCheckoutGuard(ICurrentUserService currentUser, IHttpContextAccessor httpContext,
    CustomMediator mediator, IBasketRepository baskets, IBasketMappingService mapping,
    IOrderAvailabilityGuard availability) : ICustomerCheckoutGuard
{
    public Guid? EnsureOwner(string sessionId)
    {
        CustomerBasketAccess.EnsureCustomerOrGuest(currentUser, httpContext.HttpContext);
        CustomerBasketOperationHash.Session(sessionId);
        if (currentUser.IsAuthenticated && currentUser.UserId is null)
            throw new UnauthorizedAccessException("Renew customer authentication before checkout.");
        return currentUser.UserId;
    }

    public async Task<CustomerCheckoutReviewContext> ReviewAsync(string sessionId, Guid? userId,
        CustomerCheckoutReviewDto review, CancellationToken cancellationToken)
    {
        var bootstrap = await CustomerBasketAvailability.ReadOrderingAsync(mediator, cancellationToken);
        await availability.EnsureAvailableAsync(review.Type, cancellationToken);
        if (review.PointsToRedeem > 0)
        {
            if (userId is null) throw new ForbiddenException("Sign in to redeem points.");
            if (bootstrap.Capabilities.GetValueOrDefault("loyalty") != "enabled")
                throw new NotFoundException("Customer mobile loyalty is unavailable.");
        }
        var basket = await baskets.FindBasketAsync(sessionId, userId);
        if (basket is not null) CustomerCheckoutBasketShape.Ensure(basket);
        var view = basket is null ? null : await mapping.MapAsync(basket);
        if (basket is not null && view is not null) CustomerCheckoutBasketShape.EnsureView(basket, view);
        if (!CustomerBasketRevision.Matches(view, review.ExpectedBasketId, review.ExpectedPurchaseFingerprint)
            || view is null || view.Items.Count == 0 || view.OrderType != review.Type)
            throw new ConflictException("The basket changed. Review it before checkout.");
        var currency = CurrencyCode.Normalize(bootstrap.Currency)
            ?? throw new NotFoundException("Checkout currency is unavailable.");
        var methods = new List<PaymentMethod> { PaymentMethod.Cash };
        if (review.Type == OrderType.Takeaway) methods.Add(PaymentMethod.CreditCard);
        if (bootstrap.Capabilities.GetValueOrDefault("online-payments") == "enabled"
            && (await mediator.SendQuery(new GetOnlinePaymentAvailabilityQuery(), cancellationToken)).Data?.Available == true)
            methods.Add(PaymentMethod.OnlinePayment);
        return new(view, userId, currency, methods);
    }
}
