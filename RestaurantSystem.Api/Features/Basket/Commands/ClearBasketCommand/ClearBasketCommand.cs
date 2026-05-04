using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Commands.ClearBasketCommand;

public record ClearBasketCommand(string SessionId) : ICommand<ApiResponse<BasketDto>>;


public class ClearBasketCommandHandler : ICommandHandler<ClearBasketCommand, ApiResponse<BasketDto>>
{
    private readonly IBasketService _basketService;
    private readonly ILogger<ClearBasketCommandHandler> _logger;

    public ClearBasketCommandHandler(
        IBasketService basketService,
        ILogger<ClearBasketCommandHandler> logger)
    {
        _basketService = basketService;
        _logger = logger;
    }

    public async Task<ApiResponse<BasketDto>> Handle(ClearBasketCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var basket = await _basketService.ClearBasketAsync(command.SessionId);

            _logger.LogInformation("Cleared basket for session {SessionId}", command.SessionId);

            return ApiResponse<BasketDto>.SuccessWithData(basket, "Basket cleared successfully");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to clear basket");
            return ApiResponse<BasketDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error clearing basket");
            return ApiResponse<BasketDto>.Failure("An error occurred while clearing basket");
        }
    }
}
