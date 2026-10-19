namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record CustomerCheckoutMoneyDto(
    decimal SubTotal,
    decimal Tax,
    decimal DeliveryFee,
    decimal Discount,
    decimal CustomerDiscount,
    decimal PointsDiscount,
    int PointsRedeemed,
    decimal Tip,
    decimal Total);
