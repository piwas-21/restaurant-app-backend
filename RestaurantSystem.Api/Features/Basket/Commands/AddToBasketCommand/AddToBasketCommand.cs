using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Commands.AddToBasketCommand;

public record AddToBasketCommand(
    string SessionId,
    Guid ProductId,
    Guid? ProductVariationId,
    Guid? MenuId,
    int Quantity,
    string? SpecialInstructions,
    List<Guid>? SelectedIngredients,
    List<Guid>? ExcludedIngredients,
    List<Guid>? AddedIngredients,
    Dictionary<Guid, int>? IngredientQuantities,
    List<SelectedSideItemDto>? SelectedSideItems,
    List<SelectedMenuOptionDto>? SelectedMenuOptions
) : ICommand<ApiResponse<BasketDto>>;


public class AddToBasketCommandHandler : ICommandHandler<AddToBasketCommand, ApiResponse<BasketDto>>
{
    private readonly IBasketService _basketService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AddToBasketCommandHandler> _logger;

    public AddToBasketCommandHandler(
        IBasketService basketService,
        ICurrentUserService currentUserService,
        ILogger<AddToBasketCommandHandler> logger)
    {
        _basketService = basketService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<BasketDto>> Handle(AddToBasketCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var addToBasketDto = new AddToBasketDto
            {
                ProductId = command.ProductId,
                ProductVariationId = command.ProductVariationId,
                MenuId = command.MenuId,
                Quantity = command.Quantity,
                SpecialInstructions = command.SpecialInstructions,
                SelectedIngredients = command.SelectedIngredients,
                ExcludedIngredients = command.ExcludedIngredients,
                AddedIngredients = command.AddedIngredients,
                IngredientQuantities = command.IngredientQuantities,
                SelectedSideItems = command.SelectedSideItems,
                SelectedMenuOptions = command.SelectedMenuOptions
            };

            var basket = await _basketService.AddItemToBasketAsync(
                command.SessionId,
                _currentUserService.UserId,
                addToBasketDto);

            _logger.LogInformation("Added product {ProductId} to basket for session {SessionId}",
                command.ProductId, command.SessionId);

            return ApiResponse<BasketDto>.SuccessWithData(basket, "Item added to basket successfully");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to add item to basket");
            return ApiResponse<BasketDto>.Failure(ex.Message);
        }

        // NOTE: the bare `catch (Exception)` that used to sit here was removed deliberately. It
        // turned every failure into HTTP 200 + success:false AND replaced the message with a generic
        // string — so a BadRequestException from BasketChannelGuard ("Dürüm is not available for
        // DineIn. Available for: Takeaway, Delivery.") was swallowed, destroying the actionable
        // reason this feature exists to deliver. Domain exceptions now reach the exception
        // middleware, which maps them to their proper status codes (400/403/404), and genuinely
        // unexpected exceptions surface as 500 instead of masquerading as a handled failure.
    }
}
