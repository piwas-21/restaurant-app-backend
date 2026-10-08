using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.KitchenBoard.Dtos;
using RestaurantSystem.Api.Features.KitchenBoard.Queries.GetKitchenBoardWorkQuery;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

public sealed class KitchenBoardWorkReader(
    ApplicationDbContext context,
    IOperationalQueueCursor cursor,
    ICurrentUserService currentUser,
    ILogger<KitchenBoardWorkReader> logger) : IKitchenBoardWorkReader
{
    public async Task<KitchenBoardWorkFeedDto> ReadAsync(
        GetKitchenBoardWorkQuery query,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Writers take the order-feed lock before the board lock. Take the same order here so a
        // board read cannot deadlock an amendment that updates an order and then stages its note.
        await context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended('restaurant-system.order-change-sequence', 0))",
            cancellationToken);
        await KitchenBoardSequenceLock.AcquireAsync(context, cancellationToken);

        var orderWatermark = await context.OrderChanges.AsNoTracking()
            .Select(value => (long?)value.Sequence).MaxAsync(cancellationToken) ?? 0L;
        var correctionWatermark = await context.OrderOperationalNotes
            .AsNoTracking().Select(value => (long?)value.KitchenBoardSequence)
            .MaxAsync(cancellationToken) ?? 0L;
        var completionWatermark = await context.KitchenBoardWorkCompletions.AsNoTracking()
            .Select(value => (long?)value.Sequence).MaxAsync(cancellationToken) ?? 0L;
        var filter = KitchenBoardCursorPolicy.CreateFilter(currentUser);

        var orders = await KitchenBoardOrderStream.ReadAsync(
            context, cursor, filter, query.OrdersCursor, pageSize,
            orderWatermark, logger, cancellationToken);
        var corrections = await KitchenBoardCorrectionStream.ReadAsync(
            context, cursor, filter, query.CorrectionsCursor, pageSize,
            correctionWatermark, logger, cancellationToken);
        var completions = await KitchenBoardCompletionStream.ReadAsync(
            context, cursor, filter, query.CompletionsCursor, pageSize,
            completionWatermark, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new KitchenBoardWorkFeedDto(orders, corrections, completions);
    }
}
