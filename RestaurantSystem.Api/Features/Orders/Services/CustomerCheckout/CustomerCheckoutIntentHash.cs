using System.Globalization;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

internal static class CustomerCheckoutIntentHash
{
    internal static string Compute(CustomerCheckoutIntentDto intent) => CustomerBasketOperationHash.Payload(new
    {
        Kind = "customer-order-intent-v1",
        intent.Review.SchemaVersion,
        intent.Review.ExpectedBasketId,
        intent.Review.ExpectedPurchaseFingerprint,
        intent.Review.Type,
        intent.Review.PointsToRedeem,
        Tip = intent.Review.Tip.ToString("G29", CultureInfo.InvariantCulture),
        intent.ExpectedQuoteFingerprint,
        intent.Contact,
        intent.PaymentMethod,
        intent.Address,
        intent.Notes
    });
}
