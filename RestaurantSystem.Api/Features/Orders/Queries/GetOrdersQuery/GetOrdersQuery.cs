using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using System.Linq.Expressions;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;

/// <summary>
/// Query orders with optional filters + pagination.
/// </summary>
/// <param name="Status">Comma-separated <see cref="OrderStatus"/> values (e.g. "Pending,Confirmed"). Unknown tokens ignored.</param>
/// <param name="PaymentStatus">Single <see cref="PaymentStatus"/> name.</param>
/// <param name="OrderType">Single <see cref="OrderType"/> name (DineIn/Takeaway/Delivery).</param>
/// <param name="StartDate">
/// Lower bound on <c>Order.OrderDate</c>, **inclusive** (<c>OrderDate &gt;= StartDate</c>).
/// Interpreted as UTC: <c>OrderDate</c> is stored in UTC; this value is compared verbatim with no
/// timezone conversion. Callers are responsible for building UTC instants from any local context.
/// Example — cashier "today's orders" in browser local time:
/// <code>
/// const start = new Date(); start.setHours(0, 0, 0, 0);
/// const end   = new Date(); end.setHours(24, 0, 0, 0);
/// fetch(`/api/orders?startDate=${start.toISOString()}&amp;endDate=${end.toISOString()}`);
/// </code>
/// </param>
/// <param name="EndDate">Upper bound on <c>Order.OrderDate</c>, **inclusive** (<c>OrderDate &lt;= EndDate</c>). Same UTC semantics as <see cref="StartDate"/>.</param>
/// <param name="UserId">Limit to orders owned by this user. Non-staff callers are auto-restricted to their own user-id regardless of this parameter.</param>
/// <param name="Search">Case-insensitive substring match on order number, customer name, email, or phone.</param>
/// <param name="IsFocusOrder">Filter on the cashier "focus" flag.</param>
/// <param name="TenantDay">
/// The CALENDAR DAY to list, as the cashier names it on the RESTAURANT'S wall clock — no time, no
/// zone. When present it REPLACES <see cref="StartDate"/> and <see cref="EndDate"/> (those bounds
/// are then ignored, not intersected): the handler derives the venue-day window
/// [TenantDay 00:00, TenantDay+1 00:00) on the tenant clock — the DST-safe rule of
/// <c>GetZReportQuery</c>, backend #372 — and filters <c>Order.OrderDate</c> to those UTC
/// instants, so an order created at 23:30 venue time belongs to the day the venue says it does,
/// not to whichever UTC day the instant happens to fall in. Bound as <see cref="DateOnly"/> so a
/// bare ISO <c>?tenantDay=2026-05-02</c> cannot arrive <see cref="DateTimeKind.Unspecified"/> and
/// be refused by the timestamptz column (backend #418).
/// </param>
/// <param name="ModifiedSince">Returns orders created or updated after this UTC timestamp. Used for efficient polling.</param>
/// <param name="OrderBy">Sort key: OrderDate (default), OrderNumber, Total, Status, PaymentStatus, CustomerName.</param>
/// <param name="Descending">Sort descending (default true).</param>
/// <param name="Page">1-based page index.</param>
/// <param name="PageSize">Rows per page.</param>
public record GetOrdersQuery(
    string? Status,
    string? PaymentStatus,
    string? OrderType,
    DateTime? StartDate,
    DateTime? EndDate,
    Guid? UserId,
    string? Search,
    bool? IsFocusOrder,
    DateOnly? TenantDay = null,
    DateTime? ModifiedSince = null,  // For efficient polling - returns orders modified after this timestamp
    string? OrderBy = "OrderDate",
    bool Descending = true,
    int Page = 1,
    int PageSize = 10
) : IQuery<ApiResponse<PagedResult<OrderDto>>>;

public class GetOrdersQueryHandler : IQueryHandler<GetOrdersQuery, ApiResponse<PagedResult<OrderDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantClock _clock;
    private readonly ILogger<GetOrdersQueryHandler> _logger;
    private readonly IOrderMappingService _mappingService;
    private readonly ICurrentUserService _currentUserService;

    public GetOrdersQueryHandler(
        ApplicationDbContext context,
        ITenantClock clock,
        IOrderMappingService mappingService,
        ILogger<GetOrdersQueryHandler> logger,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
        _mappingService = mappingService;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<PagedResult<OrderDto>>> Handle(GetOrdersQuery query, CancellationToken cancellationToken)
    {
        var ordersQuery = _context.Orders
            // The menu-backed half of this was missing, so those lines silently mapped with a
            // null KitchenType and null customizations. Same omission as the printer feed (#234).
            .IncludeOrderLineGraph()
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .Include(o => o.DeliveryAddress)
            .Where(o => !o.IsDeleted)
            // Sibling collection includes cartesian-multiply in EF's default single-query
            // mode, and the Menu branch multiplies against the Product branch under Items.
            .AsSplitQuery()
            .AsQueryable();

        // Staff members can see all orders, customers only see their own. The role predicate
        // lives on ICurrentUserService so this and GetOrderByIdQuery cannot drift apart.
        var isStaff = _currentUserService.IsStaff;

        // For non-staff callers, restrict to their own orders — and to NOTHING when there is no
        // caller. The guard used to also require UserId.HasValue, which read as "scope it if we
        // can" but meant an anonymous caller matched neither branch and got the whole order book
        // unfiltered. Over HTTP the controller's [Authorize] hid that, but any in-process
        // SendQuery dispatched from an [AllowAnonymous] action inherited the hole; the
        // quick-action email links did exactly that (ORDER-TYPE-AVAILABILITY-PLAN §9.20).
        if (!isStaff)
        {
            var callerId = _currentUserService.UserId;
            ordersQuery = callerId.HasValue
                ? ordersQuery.Where(o => o.UserId == callerId.Value)
                : ordersQuery.Where(_ => false);
        }

        // Apply filters - handle comma-separated status values
        if (!string.IsNullOrEmpty(query.Status))
        {
            var statusStrings = query.Status.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var statuses = new List<OrderStatus>();

            foreach (var s in statusStrings)
            {
                if (Enum.TryParse<OrderStatus>(s.Trim(), out var parsedStatus))
                {
                    statuses.Add(parsedStatus);
                }
            }

            if (statuses.Count > 0)
            {
                ordersQuery = ordersQuery.Where(o => statuses.Contains(o.Status));
            }
        }

        if (!string.IsNullOrEmpty(query.PaymentStatus) && Enum.TryParse<PaymentStatus>(query.PaymentStatus, out var paymentStatus))
        {
            ordersQuery = ordersQuery.Where(o => o.PaymentStatus == paymentStatus);
        }

        if (!string.IsNullOrEmpty(query.OrderType) && Enum.TryParse<OrderType>(query.OrderType, out var orderType))
        {
            ordersQuery = ordersQuery.Where(o => o.Type == orderType);
        }

        // Same defect class as backend #418: `?startDate=2026-08-27` binds Kind=Unspecified and
        // Npgsql refuses to compare it with the timestamptz column, so the whole listing failed
        // instead of narrowing. The bounds' documented meaning (verbatim UTC) is unchanged.
        var startDateUtc = QueryInstant.AsUtc(query.StartDate);
        var endDateUtc = QueryInstant.AsUtc(query.EndDate);
        var modifiedSinceUtc = QueryInstant.AsUtc(query.ModifiedSince);

        // A named venue day REPLACES the raw UTC bounds: the half-open window [TenantDay 00:00,
        // TenantDay+1 00:00) on the tenant's own wall clock — the till-day rule of
        // GetZReportQuery (backend #372). TenantDayWindowUtc derives the two midnights
        // independently, so a local day of 23 or 25 hours on a DST changeover is covered;
        // never startOfDay.AddDays(1).
        if (query.TenantDay.HasValue)
        {
            var (tenantDayStartUtc, tenantDayEndUtc) = _clock.TenantDayWindowUtc(query.TenantDay.Value);

            ordersQuery = ordersQuery.Where(o =>
                o.OrderDate >= tenantDayStartUtc && o.OrderDate < tenantDayEndUtc);

            // The window is logged beside the day because they are no longer the same statement,
            // and comparing the two is how an operator's "the history is missing orders" gets
            // answered — the same call the Z-report makes.
            _logger.LogInformation(
                "Filtered orders by tenant day {TenantDay} ({ZoneId}, [{StartUtc:o}, {EndUtc:o}))",
                query.TenantDay.Value,
                _clock.TimeZone.Id,
                tenantDayStartUtc,
                tenantDayEndUtc);
        }
        else
        {
            if (startDateUtc.HasValue)
            {
                ordersQuery = ordersQuery.Where(o => o.OrderDate >= startDateUtc.Value);
            }

            if (endDateUtc.HasValue)
            {
                ordersQuery = ordersQuery.Where(o => o.OrderDate <= endDateUtc.Value);
            }
        }

        if (query.UserId.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.UserId == query.UserId.Value);
        }

        if (query.IsFocusOrder.HasValue)
        {
            // Branching rather than comparing `(o.Focus != null) == value`, which EF has no
            // reason to translate into the partial index's predicate.
            ordersQuery = query.IsFocusOrder.Value
                ? ordersQuery.Where(o => o.Focus != null)
                : ordersQuery.Where(o => o.Focus == null);
        }

        // ModifiedSince filter - returns orders created or updated after the timestamp
        // Used for efficient polling to only fetch new/changed orders
        if (modifiedSinceUtc.HasValue)
        {
            ordersQuery = ordersQuery.Where(o =>
                o.CreatedAt > modifiedSinceUtc.Value ||
                (o.UpdatedAt.HasValue && o.UpdatedAt.Value > modifiedSinceUtc.Value));
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

        // None of the sort keys is unique, and a split query runs one SQL statement per
        // collection — without a tiebreaker the Skip/Take window can differ between them
        // (and pages could overlap or drop rows even in single-query mode).
        ordersQuery = query.Descending
            ? ordersQuery.OrderByDescending(keySelector).ThenBy(o => o.Id)
            : ordersQuery.OrderBy(keySelector).ThenBy(o => o.Id);

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
