using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Queries.GetBasketSummaryQuery;

public record GetBasketSummaryQuery(string SessionId) : IQuery<ApiResponse<BasketSummaryDto>>;

public class GetBasketSummaryQueryHandler : IQueryHandler<GetBasketSummaryQuery, ApiResponse<BasketSummaryDto>>
{
    private readonly IBasketService _basketService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GetBasketSummaryQueryHandler> _logger;

    public GetBasketSummaryQueryHandler(
        IBasketService basketService,
        ICurrentUserService currentUserService,
        ILogger<GetBasketSummaryQueryHandler> logger)
    {
        _basketService = basketService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<BasketSummaryDto>> Handle(GetBasketSummaryQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _basketService.GetBasketSummaryAsync(query.SessionId, _currentUserService.UserId);

            if (summary == null)
            {
                summary = new BasketSummaryDto
                {
                    Id = Guid.Empty,
                    ItemCount = 0,
                    Total = 0
                };
            }

            return ApiResponse<BasketSummaryDto>.SuccessWithData(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving basket summary");
            return ApiResponse<BasketSummaryDto>.Failure("An error occurred while retrieving basket summary");
        }
    }
}
