using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.Orders.Services;

public sealed partial class OrderRoutingService : IOrderRoutingService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantModules _modules;
    private readonly OrderRoutingSettings _settings;

    public OrderRoutingService(
        ApplicationDbContext context, ICurrentUserService currentUser,
        ITenantModules modules, IOptions<OrderRoutingSettings> settings)
    {
        _context = context;
        _currentUser = currentUser;
        _modules = modules;
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

        var routingMode = await ResolveRoutingModeAsync(cancellationToken);
        var targets = OrderRoutingTargetResolver.ResolveTargets(order, routingMode);
        foreach (var target in targets)
        {
            if (existing.ContainsKey(target))
            {
                continue;
            }

            var route = await CreateRouteAsync(order, target, cancellationToken);
            order.RoutingStates.Add(route);
            _context.OrderRoutingStates.Add(route);
            existing[target] = route;
        }
    }

    public async Task BackfillActiveReleasedRoutesAsync(CancellationToken cancellationToken)
    {
        var orderIds = await _context.Orders
            .AsNoTracking()
            .Where(order => order.IsKitchenReleased
                && order.Status != OrderStatus.Cancelled
                && order.Status != OrderStatus.Refunded
                && order.Status != OrderStatus.Completed
                && !_context.OrderRoutingStates.Any(state => state.OrderId == order.Id))
            .Select(order => order.Id)
            .ToListAsync(cancellationToken);

        foreach (var orderId in orderIds)
        {
            var order = await _context.Orders
                .IncludeOrderLineGraph()
                .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken);
            if (order is null || order.RoutingStates.Count > 0)
            {
                continue;
            }

            await EnsureRoutesAsync(order, cancellationToken);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsRouteUniqueViolation(exception))
            {
                // Another feed request won the same pre-migration race. Its route set is already
                // authoritative, so discard this context's pending graph and continue.
                _context.ChangeTracker.Clear();
            }
        }
    }

    public async Task<IReadOnlyList<OrderRoutingStateDto>> ProjectAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(order => order.Items)
                .ThenInclude(item => item.Product)
            .Include(order => order.Items)
                .ThenInclude(item => item.Menu)
                    .ThenInclude(menu => menu!.MenuItems)
                        .ThenInclude(menuItem => menuItem.Product)
            .Include(order => order.RoutingStates)
            .SingleOrDefaultAsync(item => item.Id == orderId && !item.IsDeleted, cancellationToken);
        if (order is null)
        {
            throw new NotFoundException("Order not found.");
        }

        // Releases before durable routing existed must not remain invisible forever. Only active,
        // kitchen-released orders are backfilled; terminal and held orders are intentionally left
        // without route rows so a historical read cannot create printer work.
        if (IsRoutable(order))
        {
            var routeCountBeforeBackfill = order.RoutingStates.Count;
            await EnsureRoutesAsync(order, cancellationToken);
            if (order.RoutingStates.Count > routeCountBeforeBackfill)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        await ReconcileReadinessAsync(order, cancellationToken);
        return await _context.OrderRoutingStates
            .AsNoTracking()
            .Where(state => state.OrderId == orderId)
            .OrderBy(state => state.Target)
            .Select(state => ToDto(state))
            .ToListAsync(cancellationToken);
    }

    public async Task ApplyAcknowledgementAsync(
        string deviceId, PrintAckDto acknowledgement, CancellationToken cancellationToken)
    {
        if (acknowledgement.JobType == DevicePrintJobType.Order
            && acknowledgement.Status is DevicePrintStatus.Received or DevicePrintStatus.Sent)
        {
            throw new BadRequestException("Order route acknowledgements must use a final status.");
        }

        var query = _context.OrderRoutingStates
            .Where(state => state.Target == acknowledgement.Target);
        var state = acknowledgement.JobId.HasValue
            ? await query.SingleOrDefaultAsync(candidate =>
                candidate.JobId == acknowledgement.JobId.Value
                && candidate.Revision == acknowledgement.Revision, cancellationToken)
            : await query.SingleOrDefaultAsync(candidate =>
                candidate.OrderId == acknowledgement.OrderId, cancellationToken);

        if (state is null)
        {
            if (acknowledgement.JobType == DevicePrintJobType.Order)
            {
                throw new BadRequestException("The order print job is not known to this tenant.");
            }

            // Additive update jobs do not have an OrderRoutingState; their durable identity is the
            // DeviceOrderReceipt row written by the caller.
            return;
        }

        if (state.OrderId != acknowledgement.OrderId
            || state.DeviceId is null
            || state.DeviceId != deviceId)
        {
            throw new BadRequestException("The print acknowledgement does not match its route.");
        }

        if (acknowledgement.JobType is not null
            && acknowledgement.JobType != DevicePrintJobType.Order)
        {
            throw new BadRequestException("The print acknowledgement job type does not match its route.");
        }

        // A same-status retry is idempotent, including its version and audit timestamps. This is
        // important when two printer-feed flushes race after the first response was lost.
        if (state.Status == acknowledgement.Status)
        {
            return;
        }

        if (!OrderRoutingTargetResolver.CanApply(state.Status, acknowledgement.Status))
        {
            throw new BadRequestException("The print acknowledgement is older than the route state.");
        }

        state.Status = acknowledgement.Status;
        state.FailureReason = acknowledgement.FailureReason;
        state.LastAcknowledgedAt = DateTime.UtcNow;
        state.Version++;
        state.UpdatedAt = DateTime.UtcNow;
        state.UpdatedBy = _currentUser.GetAuditIdentifier();
    }

    private async Task<OrderRoutingState> CreateRouteAsync(
        Order order, DevicePrintTarget target, CancellationToken cancellationToken)
    {
        var selection = await SelectDeviceAsync(target, cancellationToken);
        var now = DateTime.UtcNow;
        return new OrderRoutingState
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            JobId = OrderRoutingTargetResolver.CreateStableJobId(order.Id, target),
            Revision = 1,
            Version = 1,
            Target = target,
            Status = selection is null ? DevicePrintStatus.NotConfigured : DevicePrintStatus.Queued,
            DeviceId = selection,
            CreatedAt = now,
            CreatedBy = _currentUser.GetAuditIdentifier()
        };
    }

    private static OrderRoutingStateDto ToDto(OrderRoutingState state) => new(
        state.Id, state.JobId, state.Revision, state.Target, state.Status, state.DeviceId,
        state.FailureReason, state.LastAcknowledgedAt, state.Version);

    private static bool IsRouteUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (pg.ConstraintName?.Contains("OrderRoutingStates", StringComparison.OrdinalIgnoreCase) == true
            || pg.ConstraintName?.Contains("order_routing_states", StringComparison.OrdinalIgnoreCase) == true);
}
