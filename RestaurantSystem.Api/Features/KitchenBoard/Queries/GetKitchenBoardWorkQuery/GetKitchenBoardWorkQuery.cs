using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.KitchenBoard.Dtos;
using RestaurantSystem.Api.Features.KitchenBoard.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.KitchenBoard.Queries.GetKitchenBoardWorkQuery;

public sealed record GetKitchenBoardWorkQuery(
    int? PageSize = null,
    string? OrdersCursor = null,
    string? CorrectionsCursor = null,
    string? CompletionsCursor = null) : IQuery<ApiResponse<KitchenBoardWorkFeedDto>>;

public sealed class GetKitchenBoardWorkQueryHandler(
    IKitchenBoardWorkReader reader,
    IOptions<OperationalQueueSyncOptions> options,
    ITenantFeatures features)
    : IQueryHandler<GetKitchenBoardWorkQuery, ApiResponse<KitchenBoardWorkFeedDto>>
{
    public async Task<ApiResponse<KitchenBoardWorkFeedDto>> Handle(
        GetKitchenBoardWorkQuery query, CancellationToken cancellationToken)
    {
        KitchenBoardFeaturePolicy.RequireEnabled(features);
        var pageSize = query.PageSize
            ?? Math.Min(options.Value.DefaultPageSize, options.Value.MaxPageSize);
        if (pageSize is < 1 || pageSize > options.Value.MaxPageSize)
        {
            throw new BadRequestException(
                $"Kitchen board page size must be between 1 and {options.Value.MaxPageSize}.");
        }

        return ApiResponse<KitchenBoardWorkFeedDto>.SuccessWithData(
            await reader.ReadAsync(query, pageSize, cancellationToken));
    }
}
