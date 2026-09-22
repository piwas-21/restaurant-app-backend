using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

internal sealed class ServerTaskOrderReader : IServerTaskOrderReader
{
    private static readonly OrderStatus[] OverdueStatuses =
    [
        OrderStatus.Pending,
        OrderStatus.PendingApproval,
        OrderStatus.Confirmed,
        OrderStatus.Preparing,
    ];

    private static readonly DevicePrintStatus[] RoutingExceptions =
    [
        DevicePrintStatus.Failed,
        DevicePrintStatus.NotConfigured,
        DevicePrintStatus.Unknown,
        DevicePrintStatus.Skipped,
    ];

    private readonly ApplicationDbContext _context;
    private readonly IServerTaskProjector _projector;

    public ServerTaskOrderReader(ApplicationDbContext context, IServerTaskProjector projector)
    {
        _context = context;
        _projector = projector;
    }

    public async Task<List<ServerServiceTaskDto>> LoadPageAsync(
        long? upperSequence,
        DateTime serverTime,
        string bucket,
        int pageSize,
        ServerTaskPagePosition? after,
        CancellationToken cancellationToken)
    {
        var candidates = ApplyBucket(
            BuildCandidateQuery(upperSequence, serverTime), serverTime, bucket);
        var keys = candidates.Select(order => new
        {
            order.Id,
            BucketRank = order.IsKitchenReleased && !order.RoutingStates.Any()
                || order.RoutingStates.Any(state => state.IsRequired
                    && RoutingExceptions.Contains(state.Status))
                ? 2
                : order.Status == OrderStatus.Ready || order.Status == OrderStatus.OutForDelivery
                    ? 0
                    : 1,
            ActionableAt = order.IsKitchenReleased && !order.RoutingStates.Any()
                || order.RoutingStates.Any(state => state.IsRequired
                    && RoutingExceptions.Contains(state.Status))
                ? order.RoutingStates
                    .Where(state => RoutingExceptions.Contains(state.Status))
                    .Where(state => (state.UpdatedAt ?? state.CreatedAt) != default)
                    .Select(state => state.UpdatedAt ?? (DateTime?)state.CreatedAt)
                    .OrderBy(value => value)
                    .FirstOrDefault()
                    ?? (order.Status == OrderStatus.Ready || order.Status == OrderStatus.OutForDelivery
                        ? order.StatusHistory
                            .Where(history => history.ToStatus == order.Status)
                            .Select(history => (DateTime?)history.ChangedAt)
                            .OrderByDescending(value => value)
                            .FirstOrDefault() ?? order.OrderDate
                        : order.EstimatedDeliveryTime ?? order.OrderDate)
                : order.Status == OrderStatus.Ready || order.Status == OrderStatus.OutForDelivery
                    ? order.StatusHistory
                        .Where(history => history.ToStatus == order.Status)
                        .Select(history => (DateTime?)history.ChangedAt)
                        .OrderByDescending(value => value)
                        .FirstOrDefault() ?? order.OrderDate
                    : order.EstimatedDeliveryTime ?? order.OrderDate,
        });
        if (after is not null)
        {
            keys = keys.Where(candidate =>
                candidate.BucketRank > after.BucketRank
                || candidate.BucketRank == after.BucketRank
                    && (candidate.ActionableAt > after.ActionableAt
                        || candidate.ActionableAt == after.ActionableAt
                            && candidate.Id.CompareTo(after.OrderId) > 0));
        }

        var candidateRows = await keys
            .OrderBy(candidate => candidate.BucketRank)
            .ThenBy(candidate => candidate.ActionableAt)
            .ThenBy(candidate => candidate.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        if (candidateRows.Count == 0)
        {
            return [];
        }

        var orderIds = candidateRows.Select(candidate => candidate.Id).ToArray();
        var orders = await LoadOrderGraph(orderIds, cancellationToken);
        var byId = orders.ToDictionary(order => order.Id);
        return candidateRows
            .Where(candidate => byId.ContainsKey(candidate.Id))
            .Select(candidate => _projector.Project(byId[candidate.Id], serverTime))
            .ToList();
    }

    public Task<int> CountAsync(
        long? upperSequence,
        DateTime serverTime,
        string bucket,
        CancellationToken cancellationToken) =>
        ApplyBucket(BuildCandidateQuery(upperSequence, serverTime), serverTime, bucket)
            .CountAsync(cancellationToken);

    public async Task<List<ServerServiceTaskDto>> LoadByIdsAsync(
        IReadOnlyCollection<Guid> orderIds,
        DateTime serverTime,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return [];
        }

        var ids = orderIds.Distinct().ToArray();
        var orders = await BuildCandidateQuery(null, serverTime)
            .Where(order => ids.Contains(order.Id))
            .Include(order => order.StatusHistory)
            .Include(order => order.RoutingStates)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return orders.Select(order => _projector.Project(order, serverTime)).ToList();
    }

    public List<ServerServiceTaskDto> Filter(
        IEnumerable<ServerServiceTaskDto> tasks,
        string bucket) =>
        _projector.FilterAndSort(tasks, bucket);

    private static IQueryable<Order> ApplyBucket(
        IQueryable<Order> orders,
        DateTime serverTime,
        string bucket)
    {
        var requestedBucket = bucket.Trim().ToLowerInvariant();

        if (requestedBucket == "ready")
        {
            orders = orders.Where(order =>
                (order.Status == OrderStatus.Ready || order.Status == OrderStatus.OutForDelivery)
                && !(order.IsKitchenReleased && !order.RoutingStates.Any())
                && !order.RoutingStates.Any(state => state.IsRequired
                    && RoutingExceptions.Contains(state.Status)));
        }
        else if (requestedBucket == "overdue")
        {
            orders = orders.Where(order =>
                order.EstimatedDeliveryTime.HasValue
                && order.EstimatedDeliveryTime.Value <= serverTime
                && OverdueStatuses.Contains(order.Status)
                && !(order.IsKitchenReleased && !order.RoutingStates.Any())
                && !order.RoutingStates.Any(state => state.IsRequired
                    && RoutingExceptions.Contains(state.Status)));
        }
        else if (requestedBucket == "exception")
        {
            orders = orders.Where(order =>
                order.IsKitchenReleased && !order.RoutingStates.Any()
                || order.RoutingStates.Any(state => state.IsRequired
                    && RoutingExceptions.Contains(state.Status)));
        }

        return orders;
    }

    private IQueryable<Order> BuildCandidateQuery(long? upperSequence, DateTime serverTime)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Where(order => !order.IsDeleted)
            .Where(order => order.Status != OrderStatus.Completed
                && order.Status != OrderStatus.Cancelled
                && order.Status != OrderStatus.Refunded)
            .Where(order => order.Status == OrderStatus.Ready
                || order.Status == OrderStatus.OutForDelivery
                || (order.EstimatedDeliveryTime.HasValue
                    && order.EstimatedDeliveryTime.Value <= serverTime
                    && OverdueStatuses.Contains(order.Status))
                || order.RoutingStates.Any(state => state.IsRequired
                    && RoutingExceptions.Contains(state.Status))
                || (order.IsKitchenReleased && !order.RoutingStates.Any()));

        return upperSequence.HasValue
            ? query.Where(order => order.LastChangeSequence <= upperSequence.Value)
            : query;
    }

    private Task<List<Order>> LoadOrderGraph(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken) =>
        _context.Orders
            .AsNoTracking()
            .Where(order => orderIds.Contains(order.Id))
            .Include(order => order.StatusHistory)
            .Include(order => order.RoutingStates)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

}
