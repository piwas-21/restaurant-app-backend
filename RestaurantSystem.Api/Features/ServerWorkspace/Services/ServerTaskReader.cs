using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.ServerWorkspace.Queries.GetServerTasksQuery;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

/// <summary>Builds the server task snapshot/change feed from authoritative order state.</summary>
public sealed class ServerTaskReader : IServerTaskReader
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IOperationalQueueCursor _cursor;
    private readonly IServerTaskOrderReader _orders;
    private readonly TimeProvider _timeProvider;

    public ServerTaskReader(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IOperationalQueueCursor cursor,
        IServerTaskOrderReader orders,
        TimeProvider timeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _cursor = cursor;
        _orders = orders;
        _timeProvider = timeProvider;
    }

    public async Task<ServerTaskFeedDto> ReadAsync(
        GetServerTasksQuery query,
        CancellationToken cancellationToken)
    {
        var filterHash = query.FilterHash(_currentUser);
        if (string.IsNullOrWhiteSpace(query.Cursor))
        {
            return await ReadSnapshotAsync(query, filterHash, cancellationToken);
        }

        var payload = _cursor.Read(query.Cursor);
        ValidateCursor(payload, filterHash);
        return payload.Mode == OperationalQueueSyncModes.Snapshot
            ? await ReadSnapshotPageAsync(query, payload, cancellationToken)
            : await ReadChangesPageAsync(query, payload, cancellationToken);
    }

    private async Task<ServerTaskFeedDto> ReadSnapshotAsync(
        GetServerTasksQuery query,
        string filterHash,
        CancellationToken cancellationToken)
    {
        var serverTime = UtcNow;
        var upperSequence = await CurrentSequenceAsync(cancellationToken);
        var totalCount = await _orders.CountAsync(
            upperSequence, serverTime, query.NormalizedBucket, cancellationToken);
        var rows = await _orders.LoadPageAsync(
            upperSequence, serverTime, query.NormalizedBucket, query.PageSize, null, cancellationToken);
        var page = rows.Take(query.PageSize).ToList();
        var nextCursor = CreateSnapshotCursor(
            filterHash, upperSequence, serverTime, page,
            rows.Count > page.Count, 1, query.PageSize, totalCount);

        return new ServerTaskFeedDto
        {
            ServerTime = serverTime,
            Items = page,
            TotalCount = totalCount,
            NextCursor = nextCursor,
            HasMore = rows.Count > page.Count,
        };
    }

    private async Task<ServerTaskFeedDto> ReadSnapshotPageAsync(
        GetServerTasksQuery query,
        OperationalQueueCursorPayload payload,
        CancellationToken cancellationToken)
    {
        var (bucketRank, position, snapshotTime) = DecodeSnapshotPosition(payload.Position);
        var rows = await _orders.LoadPageAsync(
            payload.UpperSequence,
            snapshotTime,
            query.NormalizedBucket,
            payload.PageSize,
            new ServerTaskPagePosition(bucketRank, position, payload.PositionId!.Value),
            cancellationToken);
        var page = rows.Take(payload.PageSize).ToList();
        var nextCursor = CreateSnapshotCursor(
            payload.FilterHash, payload.UpperSequence, snapshotTime, page,
            rows.Count > page.Count, payload.Page + 1, payload.PageSize, payload.TotalCount);

        return new ServerTaskFeedDto
        {
            ServerTime = snapshotTime,
            Items = page,
            TotalCount = payload.TotalCount,
            NextCursor = nextCursor,
            HasMore = rows.Count > page.Count,
        };
    }

    private async Task<ServerTaskFeedDto> ReadChangesPageAsync(
        GetServerTasksQuery query,
        OperationalQueueCursorPayload payload,
        CancellationToken cancellationToken)
    {
        var firstPage = payload.Mode == OperationalQueueSyncModes.Watermark;
        var lowerSequence = firstPage ? payload.UpperSequence : payload.LowerSequence;
        var upperSequence = firstPage
            ? await CurrentSequenceAsync(cancellationToken)
            : payload.UpperSequence;
        var changes = _context.OrderChanges.AsNoTracking()
            .Where(change => change.Sequence > lowerSequence && change.Sequence <= upperSequence);
        if (!firstPage)
        {
            changes = changes.Where(change => change.Sequence > ParseChangePosition(payload.Position));
        }

        var rows = await changes.OrderBy(change => change.Sequence)
            .Take(payload.PageSize + 1)
            .ToListAsync(cancellationToken);
        var pageChanges = rows.Take(payload.PageSize).ToList();
        var changedIds = pageChanges.Select(change => change.OrderId).Distinct().ToArray();
        var serverTime = UtcNow;
        var matchingTasks = changedIds.Length == 0
            ? []
            : _orders.Filter(
                await _orders.LoadByIdsAsync(changedIds, serverTime, cancellationToken),
                query.NormalizedBucket);
        var totalCount = await _orders.CountAsync(
            null, serverTime, query.NormalizedBucket, cancellationToken);
        var currentTasksById = matchingTasks.ToDictionary(task => task.OrderId);
        var currentIds = currentTasksById.Keys.ToHashSet();
        var removed = changedIds.Where(id => !currentIds.Contains(id)).ToList();
        var currentTasks = changedIds
            .Where(currentTasksById.ContainsKey)
            .Select(id => currentTasksById[id])
            .ToList();
        var hasMore = rows.Count > payload.PageSize;
        var nextCursor = hasMore
            ? _cursor.Protect(new OperationalQueueCursorRequest
            {
                Mode = OperationalQueueSyncModes.Changes,
                FilterHash = payload.FilterHash,
                UpperSequence = upperSequence,
                LowerSequence = lowerSequence,
                Position = rows[payload.PageSize - 1].Sequence.ToString(CultureInfo.InvariantCulture),
                Page = payload.Page + 1,
                PageSize = payload.PageSize,
                TotalCount = totalCount,
            })
            : _cursor.Protect(new OperationalQueueCursorRequest
            {
                Mode = OperationalQueueSyncModes.Watermark,
                FilterHash = payload.FilterHash,
                UpperSequence = upperSequence,
                Page = 1,
                PageSize = payload.PageSize,
                TotalCount = totalCount,
            });

        return new ServerTaskFeedDto
        {
            ServerTime = serverTime,
            Items = currentTasks,
            TotalCount = totalCount,
            RemovedOrderIds = removed,
            NextCursor = nextCursor,
            HasMore = hasMore,
        };
    }

    private string? CreateSnapshotCursor(
        string filterHash,
        long upperSequence,
        DateTime snapshotTime,
        List<ServerServiceTaskDto> page,
        bool hasMore,
        int pageNumber,
        int pageSize,
        int totalCount) =>
        hasMore && page.Count > 0
            ? _cursor.Protect(new OperationalQueueCursorRequest
            {
                Mode = OperationalQueueSyncModes.Snapshot,
                FilterHash = filterHash,
                UpperSequence = upperSequence,
                Position = EncodeSnapshotPosition(page[^1], snapshotTime),
                PositionId = page[^1].OrderId,
                Page = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
            })
            : _cursor.Protect(new OperationalQueueCursorRequest
            {
                Mode = OperationalQueueSyncModes.Watermark,
                FilterHash = filterHash,
                UpperSequence = upperSequence,
                Page = 1,
                PageSize = pageSize,
                TotalCount = totalCount,
            });

    private static string EncodeSnapshotPosition(ServerServiceTaskDto task, DateTime snapshotTime) =>
        string.Join(':', ServerTaskProjector.BucketRank(task.Bucket), task.ActionableAt.Ticks, snapshotTime.Ticks);

    private static (int BucketRank, DateTime Position, DateTime SnapshotTime) DecodeSnapshotPosition(string? value)
    {
        var parts = value?.Split(':');
        if (parts is not [var bucket, var position, var snapshot]
            || !int.TryParse(bucket, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bucketRank)
            || bucketRank is < 0 or > 3
            || !long.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture, out var positionTicks)
            || !long.TryParse(snapshot, NumberStyles.None, CultureInfo.InvariantCulture, out var snapshotTicks))
        {
            throw InvalidCursor();
        }

        return (bucketRank, Utc(new DateTime(positionTicks)), Utc(new DateTime(snapshotTicks)));
    }

    private static long ParseChangePosition(string? value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var position)
            || position < 0)
        {
            throw InvalidCursor();
        }

        return position;
    }

    private async Task<long> CurrentSequenceAsync(CancellationToken cancellationToken) =>
        await _context.OrderChanges.Select(change => (long?)change.Sequence)
            .MaxAsync(cancellationToken) ?? 0L;

    private static void ValidateCursor(OperationalQueueCursorPayload payload, string filterHash)
    {
        if (!string.Equals(payload.FilterHash, filterHash, StringComparison.Ordinal)
            || payload.Mode is not (OperationalQueueSyncModes.Snapshot
                or OperationalQueueSyncModes.Watermark
                or OperationalQueueSyncModes.Changes))
        {
            throw InvalidCursor();
        }
    }

    private static BadRequestException InvalidCursor() => new(
        "The operational queue synchronization cursor is invalid.",
        ErrorCodes.InvalidOperationalQueueCursor);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;
}
