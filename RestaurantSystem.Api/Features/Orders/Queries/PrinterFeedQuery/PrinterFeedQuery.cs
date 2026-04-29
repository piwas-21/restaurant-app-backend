using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

// Returns confirmed orders for the printer-app feed. Authentication is
// handled at the controller boundary via [ApiKeyAuthFilter]; this handler
// runs without ICurrentUserService context (no per-user filtering).
//
// The cap of 50 mirrors the original inline query in OrdersController.
// Sorting is by OrderDate descending so the most recent confirmed orders
// arrive first when the printer comes back online.
public record PrinterFeedQuery(DateTime? ModifiedSince) : IQuery<List<OrderDto>>;

public class PrinterFeedQueryHandler : IQueryHandler<PrinterFeedQuery, List<OrderDto>>
{
    private const int MaxOrdersPerPoll = 50;

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

        // Same Include chain as the original inline query: items + product +
        // detailed ingredients + global ingredient + payments + delivery
        // address. The printer-app needs the full graph to render kitchen
        // tickets with ingredient detail.
        var ordersQuery = _context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.DetailedIngredients)
                        .ThenInclude(di => di.GlobalIngredient)
            .Include(o => o.Payments)
            .Include(o => o.DeliveryAddress)
            .Where(o => !o.IsDeleted)
            .Where(o => o.Status == OrderStatus.Confirmed)
            .AsQueryable();

        if (query.ModifiedSince.HasValue)
        {
            ordersQuery = ordersQuery.Where(o =>
                o.CreatedAt > query.ModifiedSince.Value ||
                (o.UpdatedAt.HasValue && o.UpdatedAt.Value > query.ModifiedSince.Value));
        }

        var orders = await ordersQuery
            .OrderByDescending(o => o.OrderDate)
            .Take(MaxOrdersPerPoll)
            .ToListAsync(cancellationToken);

        var orderDtos = orders.Select(_mappingService.MapToOrderDto).ToList();

        _logger.LogInformation("Printer feed returning {Count} confirmed orders", orderDtos.Count);

        return orderDtos;
    }
}
