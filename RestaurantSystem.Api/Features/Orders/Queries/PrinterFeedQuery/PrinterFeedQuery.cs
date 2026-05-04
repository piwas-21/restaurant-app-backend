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
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.DetailedIngredients)
                        .ThenInclude(di => di.GlobalIngredient)
            .Include(o => o.Payments)
            .Include(o => o.DeliveryAddress)
            .Where(o => !o.IsDeleted)
            .Where(o => o.Status == OrderStatus.Confirmed)
            .AsNoTracking()
            .AsQueryable();

        if (query.ModifiedSince.HasValue)
        {
            ordersQuery = ordersQuery.Where(o =>
                o.CreatedAt > query.ModifiedSince.Value ||
                (o.UpdatedAt.HasValue && o.UpdatedAt.Value > query.ModifiedSince.Value));
        }

        var orders = await ordersQuery
            .OrderByDescending(o => o.OrderDate)
            .Take(PrinterFeedQuery.MaxOrdersPerPoll)
            .ToListAsync(cancellationToken);

        var orderDtos = orders.Select(_mappingService.MapToOrderDto).ToList();

        _logger.LogInformation("Printer feed returning {Count} confirmed orders", orderDtos.Count);

        return orderDtos;
    }
}
