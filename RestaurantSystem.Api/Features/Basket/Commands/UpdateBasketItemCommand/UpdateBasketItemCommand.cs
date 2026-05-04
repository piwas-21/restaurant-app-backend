using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Commands.UpdateBasketItemCommand;

public record UpdateBasketItemCommand(
    string SessionId,
    Guid BasketItemId,
    int Quantity,
    string? SpecialInstructions
) : ICommand<ApiResponse<BasketDto>>;

public class UpdateBasketItemCommandHandler : ICommandHandler<UpdateBasketItemCommand, ApiResponse<BasketDto>>
{
    private readonly IBasketService _basketService;
    private readonly ILogger<UpdateBasketItemCommandHandler> _logger;

    public UpdateBasketItemCommandHandler(
        IBasketService basketService,
        ILogger<UpdateBasketItemCommandHandler> logger)
    {
        _basketService = basketService;
        _logger = logger;
    }

    public async Task<ApiResponse<BasketDto>> Handle(UpdateBasketItemCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var updateDto = new UpdateBasketItemDto
            {
                Quantity = command.Quantity,
                SpecialInstructions = command.SpecialInstructions
            };

            var basket = await _basketService.UpdateBasketItemAsync(
                command.SessionId,
                command.BasketItemId,
                updateDto);

            _logger.LogInformation("Updated basket item {BasketItemId} for session {SessionId}",
                command.BasketItemId, command.SessionId);

            return ApiResponse<BasketDto>.SuccessWithData(basket, "Basket item updated successfully");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to update basket item");
            return ApiResponse<BasketDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating basket item");
            return ApiResponse<BasketDto>.Failure("An error occurred while updating basket item");
        }
    }
}
