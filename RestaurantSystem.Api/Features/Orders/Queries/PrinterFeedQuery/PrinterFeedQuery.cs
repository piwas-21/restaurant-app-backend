using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

public record PrinterFeedQuery(DateTime? ModifiedSince) : IQuery<List<OrderDto>>
{
    public const int MaxOrdersPerPoll = 50;
}

public class PrinterFeedQueryHandler : IQueryHandler<PrinterFeedQuery, List<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderMappingService _mappingService;
    private readonly ILogger<PrinterFeedQueryHandler> _logger;

    public PrinterFeedQueryHandler(
        ApplicationDbContext context,
        IOrderMappingService mappingService,
        ILogger<PrinterFeedQueryHandler> logger)
    {
        _context = context;
        _mappingService = mappingService;
        _logger = logger;
    }

    public async Task<List<OrderDto>> Handle(PrinterFeedQuery query, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Printer feed request - modifiedSince: {Since}", query.ModifiedSince);

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
            .AsNoTracking()
            // Sibling collection includes (Items, Payments, StatusHistory) LEFT JOIN into one
            // cartesian result set in EF's default single-query mode, and the Menu branch
            // multiplies against the Product branch under Items. This endpoint is polled
            // continuously by the printer app, so the row blow-up is not a one-off cost.
            .AsSplitQuery()
            .AsQueryable();

        if (query.ModifiedSince.HasValue)
        {
            ordersQuery = ordersQuery.Where(o =>
                o.CreatedAt > query.ModifiedSince.Value ||
                (o.UpdatedAt.HasValue && o.UpdatedAt.Value > query.ModifiedSince.Value));
        }

        var orders = await ordersQuery
            .OrderByDescending(o => o.OrderDate)
            // OrderDate is not unique, and a split query runs one SQL statement per
            // collection — without a tiebreaker the Take window can differ between them.
            .ThenBy(o => o.Id)
            .Take(PrinterFeedQuery.MaxOrdersPerPoll)
            .ToListAsync(cancellationToken);

        var orderDtos = orders.Select(_mappingService.MapToOrderDto).ToList();

        _logger.LogInformation("Printer feed returning {Count} confirmed orders", orderDtos.Count);

        return orderDtos;
    }
}
