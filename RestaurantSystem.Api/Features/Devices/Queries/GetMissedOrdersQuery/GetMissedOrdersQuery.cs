using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Devices.Queries.GetMissedOrdersQuery;

/// <summary>
/// Confirmed orders between <see cref="LookbackHours"/> ago and <see cref="GraceMinutes"/> ago with
/// no <c>Printed</c> device receipt — i.e. served to the printer-feed but never printed. This is the
/// reconciliation that only the backend can do (only it knows what it served), and the exact signal
/// the 2026-07-19 incident needed: when the feed silently stopped, orders piled up
/// Confirmed-but-unprinted.
/// <para>The <b>lookback floor</b> is essential, not cosmetic: DineIn orders are created
/// <c>Confirmed</c> and are only walked forward manually, and <c>DeviceOrderReceipt</c> is a new
/// table — so without a floor, the whole back-catalogue of old Confirmed-but-never-advanced orders
/// (which have no receipt) would surface as false "missed" and bury the real, recent signal.</para>
/// </summary>
public record GetMissedOrdersQuery(int GraceMinutes, int LookbackHours)
    : IQuery<ApiResponse<List<MissedOrderDto>>>
{
    public const int MaxResults = 200;
}

public class GetMissedOrdersQueryHandler
    : IQueryHandler<GetMissedOrdersQuery, ApiResponse<List<MissedOrderDto>>>
{
    private readonly ApplicationDbContext _context;

    public GetMissedOrdersQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<ApiResponse<List<MissedOrderDto>>> Handle(
        GetMissedOrdersQuery query, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        // Must be older than the grace window (negative grace would flag every confirmed order)...
        var graceCutoff = now.AddMinutes(-Math.Max(0, query.GraceMinutes));
        // ...but not older than the lookback floor (excludes the stale Confirmed back-catalogue).
        var lookbackFloor = now.AddHours(-Math.Max(1, query.LookbackHours));

        // Served = Confirmed (the printer-feed's eligibility filter) + not soft-deleted. Explicit
        // !IsDeleted mirrors PrinterFeedQuery so the read intent is unambiguous. "Accounted for" =
        // has at least one Printed receipt on any device — a correlated !Any() so EF emits a
        // NOT EXISTS (more efficient in Postgres than NOT IN over an uncorrelated subquery).
        var missed = await _context.Orders
            .AsNoTracking()
            .Where(o => !o.IsDeleted
                && o.Status == OrderStatus.Confirmed
                && o.CreatedAt < graceCutoff
                && o.CreatedAt >= lookbackFloor
                && !_context.DeviceOrderReceipts.Any(r =>
                    r.OrderId == o.Id && r.Status == DevicePrintStatus.Printed))
            .OrderBy(o => o.OrderDate)
            .Take(GetMissedOrdersQuery.MaxResults)
            .Select(o => new MissedOrderDto(
                o.Id, o.OrderNumber, o.Type, o.TableNumber, o.OrderDate))
            .ToListAsync(cancellationToken);

        return ApiResponse<List<MissedOrderDto>>.SuccessWithData(missed);
    }
}
