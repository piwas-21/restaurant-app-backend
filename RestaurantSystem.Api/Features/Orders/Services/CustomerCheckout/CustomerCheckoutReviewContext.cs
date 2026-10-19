using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed record CustomerCheckoutReviewContext(
    BasketDto Basket,
    Guid? UserId,
    string Currency,
    IReadOnlyList<PaymentMethod> PaymentMethods);
