using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetOrderByIdQuery;

/// <summary>
/// Who the read is FOR. Defaults to the caller, so a new dispatcher of this query is scoped unless
/// it deliberately says otherwise — the opposite default is how §9.19 happened.
/// </summary>
public enum OrderReadScope
{
    /// <summary>Staff see any order; anyone else sees only their own.</summary>
    Caller,

    /// <summary>
    /// The read is not on behalf of a user at all — the server is about to email the order to the
    /// address recorded ON it. <b>Only</b> for <c>OrderEmailController</c>, whose endpoint is
    /// deliberately <c>[AllowAnonymous]</c> (ADR-004: guest checkout has no bearer token at that
    /// point) and which returns no order data in its response. Never reachable from model binding —
    /// the controller constructs the query, so this cannot be set by a request.
    /// </summary>
    SystemNotification
}

public record GetOrderByIdQuery(Guid Id, OrderReadScope Scope = OrderReadScope.Caller)
    : IQuery<ApiResponse<OrderDto>>;

/// <summary>
/// One order by id, scoped to the caller.
/// </summary>
/// <remarks>
/// The scoping is §9.19. This endpoint was <c>[Authorize]</c> and nothing else, while
/// <c>GetOrdersQuery</c> — same feature, one file over — deliberately restricts the LIST to the
/// caller's own orders. So any signed-in customer who guessed or enumerated an order id read the
/// whole thing: name, email, phone, delivery address, payment rows. The asymmetry reads as an
/// oversight rather than a decision, and it is the classic IDOR shape: authentication was checked,
/// authorization was not.
/// <para>
/// A non-owner gets the same "Order not found" a missing id gets, deliberately: a 403 would confirm
/// that the id exists, which is half of what an enumerator wants.
/// </para>
/// <para>
/// Guest orders (<c>UserId == null</c>) are readable by staff only. That is not a regression — the
/// endpoint already required authentication, so an anonymous guest could never read one anyway, and
/// the confirmation page reaches it only for a signed-in customer.
/// </para>
/// </remarks>
public class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GetOrderByIdQueryHandler> _logger;
    private readonly IOrderMappingService _mappingService;

    public GetOrderByIdQueryHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderMappingService mappingService,
        ILogger<GetOrderByIdQueryHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
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

        // Ownership is stated POSITIVELY — the caller must have an id AND it must match. Written as
        // `order.UserId != _currentUserService.UserId` it would read null == null as ownership, so a
        // caller whose id claim was missing or unparseable would be handed every GUEST order.
        var isOwner = _currentUserService.UserId is { } callerId && order.UserId == callerId;

        // Same staff predicate the LIST uses, so the two cannot answer differently about one order.
        // Logged at Warning with the ids: an enumeration attempt is the thing worth seeing in a log,
        // and it is indistinguishable from a genuine 404 in the response by design.
        if (query.Scope == OrderReadScope.Caller && !_currentUserService.IsStaff && !isOwner)
        {
            _logger.LogWarning(
                "Refused order {OrderId} to user {UserId}: not the owner", query.Id, _currentUserService.UserId);
            return ApiResponse<OrderDto>.Failure("Order not found");
        }

        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

        _logger.LogInformation("Retrieved order {OrderNumber} with ID {OrderId}", order.OrderNumber, query.Id);

        return ApiResponse<OrderDto>.SuccessWithData(orderDto);
    }
}
