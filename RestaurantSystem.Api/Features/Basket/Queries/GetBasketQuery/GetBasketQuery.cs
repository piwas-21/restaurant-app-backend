using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Queries.GetBasketQuery;

public record GetBasketQuery(string SessionId) : IQuery<ApiResponse<BasketDto>>;

public class GetBasketQueryHandler : IQueryHandler<GetBasketQuery, ApiResponse<BasketDto>>
{
    private readonly IBasketService _basketService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GetBasketQueryHandler> _logger;

    public GetBasketQueryHandler(
        IBasketService basketService,
        ICurrentUserService currentUserService,
        ILogger<GetBasketQueryHandler> logger)
    {
        _basketService = basketService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<BasketDto>> Handle(GetBasketQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var basket = await _basketService.GetBasketAsync(query.SessionId, _currentUserService.UserId);

            if (basket == null)
            {
                // Return empty basket
                basket = new BasketDto
                {
                    Id = Guid.Empty,
                    SessionId = query.SessionId,
                    Items = new List<BasketItemDto>(),
                    SubTotal = 0,
                    Tax = 0,
                    DeliveryFee = 0,
                    Discount = 0,
                    Total = 0,
                    TotalItems = 0
                };
            }

            _logger.LogInformation("Retrieved basket for session {SessionId}", query.SessionId);

            return ApiResponse<BasketDto>.SuccessWithData(basket);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving basket");
            return ApiResponse<BasketDto>.Failure("An error occurred while retrieving basket");
        }
    }
}
