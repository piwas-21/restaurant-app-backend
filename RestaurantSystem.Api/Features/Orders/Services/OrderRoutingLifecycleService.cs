using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed class OrderRoutingLifecycleService : IOrderRoutingLifecycleService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IOrderRoutingReadinessSnapshotProvider _snapshotProvider;
    private readonly IOrderRoutingReadinessService _readiness;
    private readonly OrderRoutingSettings _settings;

    public OrderRoutingLifecycleService(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IOrderRoutingReadinessSnapshotProvider snapshotProvider,
        IOrderRoutingReadinessService readiness,
        IOptions<OrderRoutingSettings> settings)
    {
        _context = context;
        _currentUser = currentUser;
        _snapshotProvider = snapshotProvider;
        _readiness = readiness;
        _settings = settings.Value;
    }

    public async Task EnsureRoutesAsync(Order order, CancellationToken cancellationToken)
    {
        if (!order.IsKitchenReleased)
        {
            return;
        }

        var existing = order.RoutingStates.ToDictionary(state => state.Target);
        if (order.Id != Guid.Empty && order.RoutingStates.Count == 0)
        {
            var persisted = await _context.OrderRoutingStates
                .Where(state => state.OrderId == order.Id)
                .ToListAsync(cancellationToken);
            foreach (var state in persisted)
            {
                existing[state.Target] = state;
            }
        }

        var readiness = await _snapshotProvider.LoadAsync(cancellationToken);
        AddMissingRoutes(order, existing, readiness);
    }

    /// <summary>
    /// Returns the durable tenant opt-in signal from the first capability-aware printer app.
    /// This activation is intentionally irreversible so a capable device cannot later claim a
    /// legacy order and print duplicate paper.
    /// </summary>
    public Task<bool> IsRoutingActivatedAsync(CancellationToken cancellationToken) =>
        _context.PrinterDeviceTargetCapabilities.AsNoTracking().AnyAsync(cancellationToken);

    public async Task BackfillActiveReleasedRoutesAsync(CancellationToken cancellationToken)
    {
        var lastOrderId = Guid.Empty;
        while (true)
        {
            var batchStartOrderId = lastOrderId;
            var orderIds = await _context.Orders
                .AsNoTracking()
                .Where(order => order.Id.CompareTo(lastOrderId) > 0
                    && order.IsKitchenReleased
                    && order.Status != OrderStatus.Cancelled
                    && order.Status != OrderStatus.Refunded
                    && order.Status != OrderStatus.Completed
                    && !_context.OrderRoutingStates.Any(state => state.OrderId == order.Id))
                .OrderBy(order => order.Id)
                .Select(order => order.Id)
                .Take(_settings.ProcessingBatchSize)
                .ToListAsync(cancellationToken);
            if (orderIds.Count == 0)
            {
                return;
            }

            lastOrderId = orderIds[^1];
            var orders = await _context.Orders
                .IncludeOrderLineGraph()
                .Include(order => order.RoutingStates)
                .Where(order => orderIds.Contains(order.Id))
                .OrderBy(order => order.Id)
                .ToListAsync(cancellationToken);
            var readiness = await _snapshotProvider.LoadAsync(cancellationToken);
            var changed = false;

            foreach (var order in orders)
            {
                if (order.RoutingStates.Count > 0)
                {
                    continue;
                }

                AddMissingRoutes(order, new Dictionary<DevicePrintTarget, OrderRoutingState>(), readiness);
                changed = true;
            }

            if (!changed)
            {
                _context.ChangeTracker.Clear();
                continue;
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                _context.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception) when (IsRouteUniqueViolation(exception))
            {
                _context.ChangeTracker.Clear();
                lastOrderId = batchStartOrderId;
            }
        }
    }

    public async Task<IReadOnlyList<OrderRoutingStateDto>> ProjectAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .IncludeOrderLineGraph()
            .Include(order => order.RoutingStates)
            .SingleOrDefaultAsync(item => item.Id == orderId && !item.IsDeleted, cancellationToken);
        if (order is null)
        {
            throw new NotFoundException("Order not found.");
        }

        // Historical released orders are backfilled, but held and terminal orders never create
        // printer work merely because a staff member viewed their routing projection.
        if (IsRoutable(order))
        {
            var routeCountBeforeBackfill = order.RoutingStates.Count;
            await EnsureRoutesAsync(order, cancellationToken);
            if (order.RoutingStates.Count > routeCountBeforeBackfill)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        await _readiness.ReconcileReadinessAsync(order, cancellationToken);
        return await _context.OrderRoutingStates
            .AsNoTracking()
            .Where(state => state.OrderId == orderId)
            .OrderBy(state => state.Target)
            .Select(state => ToDto(state))
            .ToListAsync(cancellationToken);
    }

    private void AddMissingRoutes(
        Order order,
        Dictionary<DevicePrintTarget, OrderRoutingState> existing,
        OrderRoutingReadinessSnapshot readiness)
    {
        var targets = OrderRoutingTargetResolver.ResolveTargets(order, readiness.RoutingMode);
        foreach (var target in targets)
        {
            if (existing.ContainsKey(target))
            {
                continue;
            }

            var route = CreateRoute(order, target, readiness.SelectDevice(target));
            order.RoutingStates.Add(route);
            _context.OrderRoutingStates.Add(route);
            existing[target] = route;
        }
    }

    private OrderRoutingState CreateRoute(
        Order order, DevicePrintTarget target, string? selection)
    {
        var now = DateTime.UtcNow;
        return new OrderRoutingState
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            JobId = OrderRoutingTargetResolver.CreateStableJobId(order.Id, target),
            Revision = 1,
            Version = 1,
            Target = target,
            IsRequired = target != DevicePrintTarget.Cashier,
            Status = selection is null ? DevicePrintStatus.NotConfigured : DevicePrintStatus.Queued,
            DeviceId = selection,
            CreatedAt = now,
            CreatedBy = _currentUser.GetAuditIdentifier()
        };
    }

    private static OrderRoutingStateDto ToDto(OrderRoutingState state) => new(
        state.Id, state.JobId, state.Revision, state.Target, state.Status, state.DeviceId,
        state.FailureReason, state.LastAcknowledgedAt, state.Version, state.IsRequired);

    private static bool IsRouteUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (pg.ConstraintName?.Contains("OrderRoutingStates", StringComparison.OrdinalIgnoreCase) == true
            || pg.ConstraintName?.Contains("order_routing_states", StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsRoutable(Order order) =>
        order.IsKitchenReleased
        && order.Status is not (OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.Completed);
}
