namespace RestaurantSystem.Api.Features.Basket.Dtos;

public sealed record CustomerBasketOperationResolutionDto(
    Guid OperationId, CustomerBasketOperationOutcome Outcome, BasketDto? Basket);

public enum CustomerBasketOperationOutcome { Applied, Cancelled }
