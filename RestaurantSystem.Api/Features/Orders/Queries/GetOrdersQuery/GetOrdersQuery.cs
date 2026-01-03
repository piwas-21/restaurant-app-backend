using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using System.Linq.Expressions;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;

public record GetOrdersQuery(
    string? Status,
    string? PaymentStatus,
    string? OrderType,
    DateTime? StartDate,
    DateTime? EndDate,
    Guid? UserId,
    string? Search,
    bool? IsFocusOrder,
    DateTime? ModifiedSince = null,  // For efficient polling - returns orders modified after this timestamp
    string? OrderBy = "OrderDate",
    bool Descending = true,
    int Page = 1,
    int PageSize = 10
) : IQuery<ApiResponse<PagedResult<OrderDto>>>;

public class GetOrdersQueryHandler : IQueryHandler<GetOrdersQuery, ApiResponse<PagedResult<OrderDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetOrdersQueryHandler> _logger;
    private readonly IOrderMappingService _mappingService;
    private readonly ICurrentUserService _currentUserService;

    public GetOrdersQueryHandler(
        ApplicationDbContext context, 
        IOrderMappingService mappingService, 
        ILogger<GetOrdersQueryHandler> logger,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _logger = logger;
        _mappingService = mappingService;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<PagedResult<OrderDto>>> Handle(GetOrdersQuery query, CancellationToken cancellationToken)
    {
        var ordersQuery = _context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.DetailedIngredients)
                        .ThenInclude(pi => pi.GlobalIngredient)
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .Include(o => o.DeliveryAddress)
            .Where(o => !o.IsDeleted)
            .AsQueryable();

        // Determine if user is staff (Admin, Cashier, KitchenStaff, or Server)
        // Staff members can see all orders, customers only see their own
        var isStaff = _currentUserService.IsAdmin ||
                      _currentUserService.Role == UserRole.Cashier ||
                      _currentUserService.Role == UserRole.KitchenStaff ||
                      _currentUserService.Role == UserRole.Server;

        // For non-staff users, automatically filter to their own orders
        if (!isStaff && _currentUserService.UserId.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.UserId == _currentUserService.UserId.Value);
        }

        // Apply filters
        if (!string.IsNullOrEmpty(query.Status) && Enum.TryParse<OrderStatus>(query.Status, out var status))
        {
            ordersQuery = ordersQuery.Where(o => o.Status == status);
        }

        if (!string.IsNullOrEmpty(query.PaymentStatus) && Enum.TryParse<PaymentStatus>(query.PaymentStatus, out var paymentStatus))
        {
            ordersQuery = ordersQuery.Where(o => o.PaymentStatus == paymentStatus);
        }

        if (!string.IsNullOrEmpty(query.OrderType) && Enum.TryParse<OrderType>(query.OrderType, out var orderType))
        {
            ordersQuery = ordersQuery.Where(o => o.Type == orderType);
        }

        if (query.StartDate.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.OrderDate >= query.StartDate.Value);
        }

        if (query.EndDate.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.OrderDate <= query.EndDate.Value);
        }

        if (query.UserId.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.UserId == query.UserId.Value);
        }

        if (query.IsFocusOrder.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.IsFocusOrder == query.IsFocusOrder.Value);
        }

        // ModifiedSince filter - returns orders created or updated after the timestamp
        // Used for efficient polling to only fetch new/changed orders
        if (query.ModifiedSince.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => 
                o.CreatedAt > query.ModifiedSince.Value || 
                (o.UpdatedAt.HasValue && o.UpdatedAt.Value > query.ModifiedSince.Value));
        }

        if (!string.IsNullOrEmpty(query.Search))
        {
            var searchLower = query.Search.ToLower();
            ordersQuery = ordersQuery.Where(o =>
                o.OrderNumber.ToLower().Contains(searchLower) ||
                (o.CustomerName != null && o.CustomerName.ToLower().Contains(searchLower)) ||
                (o.CustomerEmail != null && o.CustomerEmail.ToLower().Contains(searchLower)) ||
                (o.CustomerPhone != null && o.CustomerPhone.ToLower().Contains(searchLower)));
        }

        // Get total count before pagination
        var totalCount = await ordersQuery.CountAsync(cancellationToken);

        // Apply sorting
        Expression<Func<Order, object>> keySelector = query.OrderBy?.ToLower() switch
        {
            "ordernumber" => o => o.OrderNumber,
            "total" => o => o.Total,
            "status" => o => o.Status,
            "paymentstatus" => o => o.PaymentStatus,
            "customername" => o => o.CustomerName ?? "",
            _ => o => o.OrderDate
        };

        ordersQuery = query.Descending
            ? ordersQuery.OrderByDescending(keySelector)
            : ordersQuery.OrderBy(keySelector);

        // Apply pagination
        var orders = await ordersQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        // Map to DTOsa
        var orderDtos = orders.Select(_mappingService.MapToOrderDto).ToList();
        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var pagedResult = new PagedResult<OrderDto>(orderDtos, totalCount, query.Page, query.PageSize, totalPages);

        _logger.LogInformation("Retrieved {Count} orders out of {TotalCount} total", orderDtos.Count, totalCount);

        return ApiResponse<PagedResult<OrderDto>>.SuccessWithData(pagedResult);
    }
}

