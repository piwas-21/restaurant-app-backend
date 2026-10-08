using RestaurantSystem.Api.Features.KitchenBoard.Dtos;
using RestaurantSystem.Api.Features.KitchenBoard.Queries.GetKitchenBoardWorkQuery;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

public interface IKitchenBoardWorkReader
{
    Task<KitchenBoardWorkFeedDto> ReadAsync(
        GetKitchenBoardWorkQuery query, CancellationToken cancellationToken);
}
