using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Devices.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

/// <summary>
/// <paramref name="Language"/> asks for the order DETAILS (product, variation and ingredient
/// names) in a specific language — the print-language switch the printer-app sends on every poll
/// (2026-09-10 partner request; the ticket's labels were always translated, the names never were).
/// Values: a print-safe code (en/de/fr/it/es/nl/tr) resolves every name in that language with the
/// frozen checkout name as fallback; "auto" resolves per order from the order's own
/// PreferredLanguage within the same set; anything else (or absent) is today's behaviour — the
/// frozen single-language names. See <see cref="OrderDisplayTranslator"/>.
/// </summary>
public record PrinterFeedQuery(
    DateTime? ModifiedSince,
    string? Language = null,
    string? DeviceId = null) : IQuery<List<OrderDto>>
{
    public const int MaxOrdersPerPoll = 50;
}

public partial class PrinterFeedQueryHandler : IQueryHandler<PrinterFeedQuery, List<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderMappingService _mappingService;
    private readonly IOrderDisplayTranslator _displayTranslator;
    private readonly IOrderRoutingService _routing;
    private readonly ILogger<PrinterFeedQueryHandler> _logger;

    public PrinterFeedQueryHandler(
        ApplicationDbContext context,
        IOrderMappingService mappingService,
        IOrderDisplayTranslator displayTranslator,
        IOrderRoutingService routing,
        ILogger<PrinterFeedQueryHandler> logger)
    {
        _context = context;
        _mappingService = mappingService;
        _displayTranslator = displayTranslator;
        _routing = routing;
        _logger = logger;
    }

    public async Task<List<OrderDto>> Handle(PrinterFeedQuery query, CancellationToken cancellationToken)
    {
        var deviceId = NormalizeDeviceId(query.DeviceId);

        _logger.LogInformation("Printer feed request - modifiedSince: {Since}, device: {DeviceId}",
            query.ModifiedSince, deviceId ?? "legacy");

        var routing = await PrepareRoutingAsync(deviceId, cancellationToken);
        var ordersQuery = BuildOrdersQuery(routing);
        ordersQuery = ApplyModifiedSince(ordersQuery, query.ModifiedSince);
        ordersQuery = ApplyLanguageIncludes(ordersQuery, query.Language);
        var orders = await ReadOrdersAsync(ordersQuery, cancellationToken);

        if (!string.IsNullOrWhiteSpace(query.Language))
        {
            _displayTranslator.Apply(orders, query.Language);
        }

        var orderDtos = MapOrders(orders);

        _logger.LogInformation("Printer feed returning {Count} confirmed orders for {Device}",
            orderDtos.Count, deviceId ?? "legacy");

        return orderDtos;
    }

    private static string? NormalizeDeviceId(string? rawDeviceId)
    {
        var deviceId = DeviceIdNormalizer.Normalize(rawDeviceId);
        if (rawDeviceId is not null && deviceId is null)
        {
            throw new BadRequestException("The X-Device-Id header cannot be empty.");
        }

        return deviceId;
    }

    private async Task<RoutingContext> PrepareRoutingAsync(
        string? deviceId, CancellationToken cancellationToken)
    {
        if (deviceId is not null)
        {
            await ValidateRegisteredDeviceAsync(deviceId, cancellationToken);
            // A released order may have been created before this installation first reported its
            // capabilities. Reconcile only this device's pending routes before reading the feed so
            // an offline release becomes printable after the device comes back online.
            await _routing.BackfillActiveReleasedRoutesAsync(cancellationToken);
            await _routing.ReconcileDeviceRoutesAsync(deviceId, cancellationToken);
        }

        var routingActivated = deviceId is null
            && await _routing.IsRoutingActivatedAsync(cancellationToken);
        if (routingActivated)
        {
            // Rollout reconciliation: the first capability-aware heartbeat is the tenant's
            // durable opt-in. Backfill before applying suppression so pre-migration released
            // orders cannot leak through the legacy broadcast projection.
            await _routing.BackfillActiveReleasedRoutesAsync(cancellationToken);
        }

        return new RoutingContext(deviceId, routingActivated);
    }

    private async Task ValidateRegisteredDeviceAsync(
        string deviceId, CancellationToken cancellationToken)
    {
        if (deviceId.Length > DeviceIdNormalizer.MaxLength)
        {
            throw new BadRequestException("The X-Device-Id header is too long.");
        }

        var knownDevice = await _context.PrinterDevices
            .AsNoTracking()
            .AnyAsync(device => device.DeviceId == deviceId, cancellationToken);
        if (!knownDevice)
        {
            throw new BadRequestException("The X-Device-Id header is not registered.");
        }
    }

    private IQueryable<Order> BuildOrdersQuery(RoutingContext routing)
    {
        // Explicit !IsDeleted mirrors the original inline code; the global query filter would
        // also handle this but the read intent stays unambiguous when grepping delete-aware paths.
        var ordersQuery = _context.Orders
            // Covers both product-backed and menu-backed line-resolution paths.
            .IncludeOrderLineGraph()
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .Include(o => o.DeliveryAddress)
            .Where(o => !o.IsDeleted)
            .Where(o => o.Status == OrderStatus.Confirmed)
            .Where(o => o.IsKitchenReleased)
            .AsNoTracking()
            .AsSplitQuery()
            .AsQueryable();

        if (routing.DeviceId is not null)
        {
            // Device-aware clients receive legacy/unrouted orders plus only Queued route jobs
            // assigned to this device. Missing X-Device-Id keeps the legacy projection.
            return ordersQuery
                .Include(o => o.RoutingStates.Where(state =>
                    state.DeviceId == routing.DeviceId && state.Status == DevicePrintStatus.Queued))
                .Where(o => !o.RoutingStates.Any()
                    || o.RoutingStates.Any(state => state.DeviceId == routing.DeviceId
                        && state.Status == DevicePrintStatus.Queued));
        }

        return routing.RoutingActivated
            ? ordersQuery.Where(o => !o.RoutingStates.Any())
            : ordersQuery;
    }

    private static IQueryable<Order> ApplyModifiedSince(
        IQueryable<Order> ordersQuery, DateTime? modifiedSince)
    {
        // A cursor with no offset binds Unspecified; normalize it before comparing to timestamptz.
        var modifiedSinceUtc = QueryInstant.AsUtc(modifiedSince);
        return modifiedSinceUtc.HasValue
            ? ordersQuery.Where(o => o.CreatedAt > modifiedSinceUtc.Value
                || (o.UpdatedAt.HasValue && o.UpdatedAt.Value > modifiedSinceUtc.Value))
            : ordersQuery;
    }

    private static IQueryable<Order> ApplyLanguageIncludes(
        IQueryable<Order> ordersQuery, string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return ordersQuery;
        }

        return ordersQuery
            .Include(o => o.Items).ThenInclude(i => i.Product!.Descriptions)
            .Include(o => o.Items).ThenInclude(i => i.Product!.DetailedIngredients)
                .ThenInclude(pi => pi.Descriptions)
            .Include(o => o.Items).ThenInclude(i => i.ProductVariation!.Descriptions)
            .AsSplitQuery();
    }

    private static Task<List<Order>> ReadOrdersAsync(
        IQueryable<Order> ordersQuery, CancellationToken cancellationToken) => ordersQuery
        .OrderByDescending(o => o.OrderDate)
        .ThenBy(o => o.Id)
        .Take(PrinterFeedQuery.MaxOrdersPerPoll)
        .ToListAsync(cancellationToken);
}
