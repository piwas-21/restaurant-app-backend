using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Final enforcement of per-order-type availability at order creation — the last line of defence.
/// </summary>
/// <remarks>
/// Needed independently of the basket guard because the waiter flow (<c>createServerOrder</c>) posts
/// items straight to <c>POST /api/Orders</c> and never touches a basket, and because a stale tab or
/// tampered payload must not be able to create an unfulfillable order.
/// <para>
/// Staff get <b>warn-and-allow</b>, not a block: a waiter genuinely does need to plate a
/// takeaway-only item for a guest at a table, and hard-blocking them would earn a support ticket in
/// week one. Any authenticated staff account qualifies (not role-gated beyond "not a Customer") —
/// the override is recorded, not restricted. Recorded means PERSISTED since §9.6: the guard returns
/// what it let through and <c>CreateOrderCommandHandler</c> stamps it onto the order.
/// </para>
/// </remarks>
public class OrderChannelGuard : IOrderChannelGuard
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<OrderChannelGuard> _logger;

    public OrderChannelGuard(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<OrderChannelGuard> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<OrderChannelOverride?> EnsureOrderableAsync(
        IReadOnlyCollection<CreateOrderItemDto> items,
        OrderType orderType,
        CancellationToken cancellationToken = default)
    {
        // Walk ChildItems too. BasketToOrderTranslator puts BOTH bundle children and top-level side
        // items there, so checking only the top level would let a takeaway-only product through as a
        // bundle option or a side item — through the basket path and a direct POST /api/Orders alike.
        var productIds = FlattenProductIds(items).ToList();
        if (productIds.Count == 0)
        {
            return null;
        }

        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var blocked = products
            .Where(p => !OrderChannelMap.Allows(OrderTypeAvailability.EffectiveMask(p), orderType))
            .ToList();

        if (blocked.Count == 0)
        {
            return null;
        }

        var names = string.Join(", ", blocked.Select(p => p.Name));

        if (IsStaff())
        {
            // Logged AND returned: the log is the operational trace (it carries the role and the
            // count), the return value is the durable one the caller stamps onto the order. The log
            // alone was the whole record until §9.6, and no owner reads application logs.
            _logger.LogWarning(
                "STAFF ORDER-TYPE OVERRIDE by user {UserId} (role {Role}): {Count} item(s) not available "
                + "for {OrderType} were accepted ({Names})",
                _currentUserService.UserId, _currentUserService.Role, blocked.Count, orderType, names);

            return new OrderChannelOverride(_currentUserService.GetAuditIdentifier(), names);
        }

        throw new BadRequestException(
            $"Not available for {orderType}: {names}. Please change your order type or remove these items.");
    }

    private static IEnumerable<Guid> FlattenProductIds(IEnumerable<CreateOrderItemDto>? items)
    {
        if (items is null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            if (item.ProductId.HasValue)
            {
                yield return item.ProductId.Value;
            }

            foreach (var childId in FlattenProductIds(item.ChildItems))
            {
                yield return childId;
            }
        }
    }

    // Any authenticated non-Customer account is staff. Role is null for anonymous callers.
    private bool IsStaff() =>
        _currentUserService.IsAuthenticated
        && _currentUserService.Role is not null
        && _currentUserService.Role != UserRole.Customer;
}
