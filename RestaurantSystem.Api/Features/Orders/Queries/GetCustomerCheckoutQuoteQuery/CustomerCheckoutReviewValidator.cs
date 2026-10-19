using FluentValidation;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCustomerCheckoutQuoteQuery;

public sealed class CustomerCheckoutReviewValidator : AbstractValidator<CustomerCheckoutReviewDto>
{
    public CustomerCheckoutReviewValidator()
    {
        RuleFor(review => review.SchemaVersion).Equal(1);
        RuleFor(review => review.ExpectedBasketId).NotEmpty();
        RuleFor(review => review.ExpectedPurchaseFingerprint).Must(BasketPurchaseFingerprint.IsValidDigest);
        // Dine-in submission uses the separate QR participant/round contract.
        RuleFor(review => review.Type).Must(type => type is OrderType.Takeaway or OrderType.Delivery);
        RuleFor(review => review.PointsToRedeem).GreaterThanOrEqualTo(0);
        RuleFor(review => review.Tip).GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(OrderFieldLimits.MaximumMoneyAmount)
            .PrecisionScale(10, 2, ignoreTrailingZeros: true);
    }
}
