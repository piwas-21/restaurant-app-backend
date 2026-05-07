using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetOrderByIdQuery;

public record GetOrderByIdQuery(Guid Id) : IQuery<ApiResponse<OrderDto>>;

public class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetOrderByIdQueryHandler> _logger;
    private readonly IOrderMappingService _mappingService;

    public GetOrderByIdQueryHandler(ApplicationDbContext context, IOrderMappingService mappingService, ILogger<GetOrderByIdQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
        _mappingService = mappingService;
    }

    public async Task<ApiResponse<OrderDto>> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Include(o => o.Items)
                .ThenInclude(i => i.ProductVariation)
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .Include(o => o.DeliveryAddress)
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == query.Id && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            _logger.LogWarning("Order with ID {OrderId} not found", query.Id);
            return ApiResponse<OrderDto>.Failure("Order not found");
        }

        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

        _logger.LogInformation("Retrieved order {OrderNumber} with ID {OrderId}", order.OrderNumber, query.Id);

        return ApiResponse<OrderDto>.SuccessWithData(orderDto);
    }
}
