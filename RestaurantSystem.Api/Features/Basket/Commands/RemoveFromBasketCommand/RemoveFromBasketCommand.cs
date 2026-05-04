using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Commands.RemoveFromBasketCommand;

public record RemoveFromBasketCommand(
    string SessionId,
    Guid BasketItemId
) : ICommand<ApiResponse<BasketDto>>;

public class RemoveFromBasketCommandHandler : ICommandHandler<RemoveFromBasketCommand, ApiResponse<BasketDto>>
{
    private readonly IBasketService _basketService;
    private readonly ILogger<RemoveFromBasketCommandHandler> _logger;

    public RemoveFromBasketCommandHandler(
        IBasketService basketService,
        ILogger<RemoveFromBasketCommandHandler> logger)
    {
        _basketService = basketService;
        _logger = logger;
    }

    public async Task<ApiResponse<BasketDto>> Handle(RemoveFromBasketCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var basket = await _basketService.RemoveItemFromBasketAsync(
                command.SessionId,
                command.BasketItemId);

            _logger.LogInformation("Removed basket item {BasketItemId} from session {SessionId}",
                command.BasketItemId, command.SessionId);

            return ApiResponse<BasketDto>.SuccessWithData(basket, "Item removed from basket successfully");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to remove item from basket");
            return ApiResponse<BasketDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error removing item from basket");
            return ApiResponse<BasketDto>.Failure("An error occurred while removing item from basket");
        }
    }
}
