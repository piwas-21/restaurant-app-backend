using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Devices.Services;
using RestaurantSystem.Domain.Common.Enums;
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

public class PrinterFeedQueryHandler : IQueryHandler<PrinterFeedQuery, List<OrderDto>>
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
        var deviceId = DeviceIdNormalizer.Normalize(query.DeviceId);
        if (query.DeviceId is not null && deviceId is null)
        {
            throw new BadRequestException("The X-Device-Id header cannot be empty.");
        }

        _logger.LogInformation("Printer feed request - modifiedSince: {Since}, device: {DeviceId}",
            query.ModifiedSince, deviceId ?? "legacy");

        if (deviceId is not null)
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

            // A released order may have been created before this installation first reported its
            // capabilities. Reconcile only this device's pending routes before reading the feed so
            // an offline release becomes printable after the device comes back online.
            await _routing.BackfillActiveReleasedRoutesAsync(cancellationToken);
            await _routing.ReconcileDeviceRoutesAsync(deviceId, cancellationToken);
        }

        // Explicit !IsDeleted filter mirrors the original inline code; the
        // global query filter would also handle this but we keep it explicit
        // so the read intent is unambiguous when grepping for delete-aware paths.
        var ordersQuery = _context.Orders
            // Covers BOTH line-resolution paths. The menu-backed one was missing: the mapper
            // reads it null-conditionally, so KitchenType came back null — and the printer app
            // routes kitchen tickets by KitchenType, so those lines printed on NEITHER kitchen
            // printer rather than merely losing their customizations.
            .IncludeOrderLineGraph()
            .Include(o => o.Payments)
            // Order.StatusHistory is initialized non-null on the entity, so the mapper's
            // `?? new List<>()` guard can never fire — omitting the include silently
            // emitted [] instead of the history. Mirrors GetOrdersQuery/GetOrderByIdQuery.
            .Include(o => o.StatusHistory)
            .Include(o => o.DeliveryAddress)
            .Where(o => !o.IsDeleted)
            .Where(o => o.Status == OrderStatus.Confirmed)
            // A held counter sale is a real unpaid order, but it is not a kitchen ticket yet.
            // Legacy rows are migrated with this flag true.
            .Where(o => o.IsKitchenReleased)
            .AsNoTracking()
            // Sibling collection includes (Items, Payments, StatusHistory) LEFT JOIN into one
            // cartesian result set in EF's default single-query mode, and the Menu branch
            // multiplies against the Product branch under Items. This endpoint is polled
            // continuously by the printer app, so the row blow-up is not a one-off cost.
            .AsSplitQuery()
            .AsQueryable();

        if (deviceId is not null)
        {
            // Device-aware clients receive legacy/unrouted orders plus only Queued route jobs
            // assigned to this device. The filtered include is intentional: returning terminal,
            // uncertain, or another device's states would make the client print a completed job or
            // fall back to a broadcast path. Missing X-Device-Id keeps the legacy projection.
            ordersQuery = ordersQuery
                .Include(o => o.RoutingStates.Where(state =>
                    state.DeviceId == deviceId && state.Status == DevicePrintStatus.Queued))
                .Where(o => !o.RoutingStates.Any()
                    || o.RoutingStates.Any(state => state.DeviceId == deviceId
                        && state.Status == DevicePrintStatus.Queued));
        }

        // Kind-normalised first: a cursor with no offset (`?modifiedSince=2026-08-27`) binds
        // Unspecified, which Npgsql will not compare with the timestamptz column — the poll then
        // throws and the printer stops receiving tickets altogether (backend #418).
        var modifiedSinceUtc = QueryInstant.AsUtc(query.ModifiedSince);

        if (modifiedSinceUtc.HasValue)
        {
            ordersQuery = ordersQuery.Where(o =>
                o.CreatedAt > modifiedSinceUtc.Value ||
                (o.UpdatedAt.HasValue && o.UpdatedAt.Value > modifiedSinceUtc.Value));
        }

        // Name translations need the per-language description rows. They ride ONLY on this feed's
        // query (a polled endpoint, not the admin list), and only when a language was asked for —
        // without a language the poll costs exactly what it always did.
        if (!string.IsNullOrWhiteSpace(query.Language))
        {
            ordersQuery = ordersQuery
                .Include(o => o.Items).ThenInclude(i => i.Product!.Descriptions)
                .Include(o => o.Items).ThenInclude(i => i.Product!.DetailedIngredients)
                    .ThenInclude(pi => pi.Descriptions)
                .Include(o => o.Items).ThenInclude(i => i.ProductVariation!.Descriptions);
        }

        var orders = await ordersQuery
            .OrderByDescending(o => o.OrderDate)
            // OrderDate is not unique, and a split query runs one SQL statement per
            // collection — without a tiebreaker the Take window can differ between them.
            .ThenBy(o => o.Id)
            .Take(PrinterFeedQuery.MaxOrdersPerPoll)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(query.Language))
        {
            _displayTranslator.Apply(orders, query.Language);
        }

        var orderDtos = orders.Select(_mappingService.MapToOrderDto).ToList();

        // The guest status poll's token is the GUEST'S secret, minted for one read-only screen.
        // It has no business on printer wire (paper, network captures, spooler logs) and the
        // printer renders nothing it would read — strip it from this feed's projection.
        foreach (var dto in orderDtos)
        {
            dto.GuestStatusToken = null;
        }

        _logger.LogInformation("Printer feed returning {Count} confirmed orders for {Device}",
            orderDtos.Count, deviceId ?? "legacy");

        return orderDtos;
    }

}
