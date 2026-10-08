using Microsoft.Extensions.Logging;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal sealed class KitchenBoardStreamReadContext
{
    public required ApplicationDbContext Context { get; init; }

    public required IOperationalQueueCursor Cursor { get; init; }

    public required string FilterHash { get; init; }

    public required string? CursorValue { get; init; }

    public required int PageSize { get; init; }

    public required long CurrentWatermark { get; init; }

    public required ILogger Logger { get; init; }

    public required CancellationToken CancellationToken { get; init; }
}
