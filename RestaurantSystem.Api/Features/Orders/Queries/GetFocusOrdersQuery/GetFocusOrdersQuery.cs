using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetFocusOrdersQuery;

public class GetFocusOrdersQuery : IQuery<ApiResponse<List<OrderDto>>>
{
    public bool? ActiveOnly { get; set; } = true;
    public int? Priority { get; set; }
    public string? OrderBy { get; set; } = "Priority"; // Priority, OrderDate, FocusedAt
}

public class GetFocusOrdersQueryHandler : IQueryHandler<GetFocusOrdersQuery, ApiResponse<List<OrderDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetFocusOrdersQueryHandler> _logger;
    private readonly IOrderMappingService _mappingService;

    public GetFocusOrdersQueryHandler(
        ApplicationDbContext context,
        ILogger<GetFocusOrdersQueryHandler> logger,
        IOrderMappingService mappingService)
    {
        _context = context;
        _logger = logger;
        _mappingService = mappingService;
    }

    public async Task<ApiResponse<List<OrderDto>>> Handle(GetFocusOrdersQuery query, CancellationToken cancellationToken)
    {
        var ordersQuery = _context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.DetailedIngredients)
                        .ThenInclude(pi => pi.GlobalIngredient)
            // Menu-backed lines resolve KitchenType + ingredient customizations through
            // Menu -> MenuItems -> Product; without the chain the mapper silently emits
            // null for both. Same omission as the printer feed (#234).
            .Include(o => o.Items)
                .ThenInclude(i => i.Menu)
                    .ThenInclude(m => m!.MenuItems)
                        .ThenInclude(mi => mi.Product)
                            .ThenInclude(p => p.DetailedIngredients)
                                .ThenInclude(di => di.GlobalIngredient)
            .Include(o => o.Payments)
            // StatusHistory is initialized non-null on the entity, so the mapper's
            // `?? new List<>()` guard never fires — omitting it silently emitted [].
            .Include(o => o.StatusHistory)
            .Include(o => o.DeliveryAddress)
            // Sibling collection includes cartesian-multiply in EF's default single-query
            // mode, and the Menu branch multiplies against the Product branch under Items.
            .AsSplitQuery()
            .Where(o => !o.IsDeleted && o.IsFocusOrder);

        // Filter by active status
        if (query.ActiveOnly == true)
        {
            ordersQuery = ordersQuery.Where(o =>
                o.Status != OrderStatus.Completed &&
                o.Status != OrderStatus.Cancelled);
        }

        // Filter by priority
        if (query.Priority.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.Priority == query.Priority.Value);
        }

        // Apply ordering
        ordersQuery = query.OrderBy?.ToLower() switch
        {
            "priority" => ordersQuery
                .OrderBy(o => o.Priority ?? 999)
                .ThenBy(o => o.FocusedAt),
            "orderdate" => ordersQuery.OrderByDescending(o => o.OrderDate),
            "focusedat" => ordersQuery.OrderByDescending(o => o.FocusedAt),
            _ => ordersQuery.OrderBy(o => o.Priority ?? 999).ThenBy(o => o.FocusedAt)
        };

        var orders = await ordersQuery.ToListAsync(cancellationToken);

        var orderDtos = orders.Select(o => _mappingService.MapToOrderDto(o)).ToList();

        _logger.LogInformation("Retrieved {Count} focus orders", orderDtos.Count);

        return ApiResponse<List<OrderDto>>.SuccessWithData(orderDtos,
            $"Retrieved {orderDtos.Count} focus orders");
    }
}
