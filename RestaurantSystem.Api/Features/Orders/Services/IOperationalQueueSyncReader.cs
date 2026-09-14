using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOperationalQueueSyncReader
{
    Task<ApiResponse<PagedResult<OrderDto>>> ReadSnapshotAsync(
        GetOrdersQuery query, string filterHash, CancellationToken cancellationToken);
    Task<ApiResponse<PagedResult<OrderDto>>> ReadSnapshotNextAsync(
        GetOrdersQuery query, OperationalQueueCursorPayload payload, CancellationToken cancellationToken);
    Task<ApiResponse<PagedResult<OrderDto>>> ReadChangesAsync(
        GetOrdersQuery query, OperationalQueueCursorPayload payload, CancellationToken cancellationToken);
}
