using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetOrderByIdQuery;

/// <param name="Id">Order to load.</param>
/// <param name="EnforceOwnership">
/// When true (the default, and the only value any user-facing route should use), a
/// non-staff caller may only read an order they own; everyone else gets the
/// not-found response. Set to false ONLY for a trusted server-side caller that
/// returns no order data to the requester — currently just the [AllowAnonymous]
/// confirmation-email endpoint, which must resolve guest orders (UserId == null)
/// for a caller who has no token at all. Defaulting to true keeps a new route
/// secure unless it explicitly opts out.
///
/// [BindNever] because sibling query records in this codebase are bound straight
/// off the query string ([FromQuery] GetOrdersQuery, GetFocusOrdersQuery, …). Both
/// callers construct this one by hand today, but if someone later normalises the
/// route to [FromQuery] then `?enforceOwnership=false` would silently reopen the
/// IDOR this record exists to close. Refuse the binding rather than rely on nobody
/// making that edit. Applied to BOTH the parameter and the property: a positional
/// record binds through its constructor parameter, so the property-only form would
/// leave the very path this guards against uncovered.
/// </param>
public record GetOrderByIdQuery(
    Guid Id,
    [BindNever][property: BindNever] bool EnforceOwnership = true)
    : IQuery<ApiResponse<OrderDto>>;

public class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetOrderByIdQueryHandler> _logger;
    private readonly IOrderMappingService _mappingService;
    private readonly ICurrentUserService _currentUserService;

    public GetOrderByIdQueryHandler(
        ApplicationDbContext context,
        IOrderMappingService mappingService,
        ILogger<GetOrderByIdQueryHandler> logger,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _logger = logger;
        _mappingService = mappingService;
        _currentUserService = currentUserService;
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

        if (query.EnforceOwnership && !CanCurrentUserRead(order))
        {
            // Same response and status as a genuinely missing order, on purpose: a distinct
            // 403 would confirm the id exists and turn this endpoint into an oracle for
            // enumerating real orders. The reason is recorded server-side only.
            _logger.LogWarning(
                "User {UserId} denied access to order {OrderId} they do not own; responding as not-found",
                _currentUserService.UserId,
                query.Id);
            return ApiResponse<OrderDto>.Failure("Order not found");
        }

        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

        _logger.LogInformation("Retrieved order {OrderNumber} with ID {OrderId}", order.OrderNumber, query.Id);

        return ApiResponse<OrderDto>.SuccessWithData(orderDto);
    }

    /// <summary>
    /// Staff read any order; a customer reads only their own. Mirrors the scoping
    /// <c>GetOrdersQuery</c> applies to the list, so a single order cannot be reached
    /// through this route when it would be filtered out of the list.
    /// </summary>
    private bool CanCurrentUserRead(Order order)
    {
        if (_currentUserService.IsStaff)
        {
            return true;
        }

        // An unauthenticated caller owns nothing. Guest orders (UserId == null) are
        // deliberately unreachable here — matching a null owner against a null caller
        // would let any anonymous request read every guest order in the system.
        var userId = _currentUserService.UserId;
        return userId.HasValue && order.UserId == userId.Value;
    }
}
