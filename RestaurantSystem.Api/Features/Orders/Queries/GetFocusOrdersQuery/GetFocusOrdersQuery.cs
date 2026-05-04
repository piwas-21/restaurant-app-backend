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
            .Include(o => o.Payments)
            .Include(o => o.DeliveryAddress)
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
