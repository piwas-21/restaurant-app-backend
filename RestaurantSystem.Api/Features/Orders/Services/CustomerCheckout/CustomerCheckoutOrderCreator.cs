using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerCheckoutOrderCreator(ICustomerCheckoutPreparation preparation, IOrderFactory orders,
    IOrderCreationWriter writer, IBasketService baskets, IPreferredLanguageCapture languages) : ICustomerCheckoutOrderCreator
{
    public async Task<CustomerCheckoutAccepted> CreateUnderLockAsync(string sessionId, CustomerCheckoutIntentDto intent,
        CancellationToken cancellationToken)
    {
        var review = await preparation.ReviewUnderLockAsync(sessionId, intent, cancellationToken);
        var command = CustomerCheckoutCommandFactory.Create(intent, review.Prepared, review.Quote);
        var language = await languages.ForUserAsync(review.Context.UserId, cancellationToken);
        var draft = await orders.CreateAsync(command, review.Context.UserId, language, cancellationToken);
        if (draft.IsFailed) throw new BadRequestException(draft.Error);
        var error = await writer.WriteNativeAsync(command, draft, review.Context.Currency, cancellationToken);
        if (error is not null) throw new BadRequestException(error);
        CustomerCheckoutMoneyGuard.Ensure(draft.Order);
        var actual = CustomerCheckoutProjection.Create(review.Context, intent.Review, draft.Order);
        if (!string.Equals(actual.QuoteFingerprint, review.Quote.QuoteFingerprint, StringComparison.Ordinal))
            throw new ConflictException("Checkout changed during acceptance. Review the current total.");
        await baskets.ClearBasketAsync(sessionId);
        return new(draft.Order, draft.PaysOnline);
    }
}
