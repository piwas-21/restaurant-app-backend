using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetReservationsQuery;

/// <param name="Date">
/// The CALENDAR DAY to filter on, as the operator names it — no time, no zone. Bound as
/// <see cref="DateOnly"/> on purpose: a <c>DateTime</c> here bound <c>?date=2026-08-27</c> with
/// <see cref="DateTimeKind.Unspecified"/>, which Npgsql refuses to write to the
/// <c>timestamptz</c> column, so EVERY dated call failed and the dashboard's day view showed
/// nothing (backend #418).
/// </param>
public record GetReservationsQuery(
    DateOnly? Date = null,
    Guid? TableId = null,
    ReservationStatus? Status = null,
    Guid? CustomerId = null,
    int Page = 1,
    int PageSize = 50
) : IQuery<ApiResponse<PagedResult<ReservationDto>>>;

public class GetReservationsQueryHandler : IQueryHandler<GetReservationsQuery, ApiResponse<PagedResult<ReservationDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetReservationsQueryHandler> _logger;

    public GetReservationsQueryHandler(ApplicationDbContext context, ILogger<GetReservationsQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<ReservationDto>>> Handle(GetReservationsQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var reservationsQuery = _context.Reservations
                .Include(r => r.Table)
                .AsQueryable();

            // Apply filters
            if (query.Date.HasValue)
            {
                // ReservationDate is the booked CALENDAR DAY stored at midnight UTC (see the
                // entity: "never run it through ITenantClock"), so the day is matched as the
                // half-open UTC window it is written in — NOT as a tenant-local day window, which
                // would offset every stored value by the zone and answer with the wrong day.
                // A window rather than `r.ReservationDate.Date ==`: that wraps the column in
                // date_trunc and cannot use the ReservationDate index.
                var dayStartUtc = query.Date.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var nextDayStartUtc = dayStartUtc.AddDays(1);

                reservationsQuery = reservationsQuery.Where(
                    r => r.ReservationDate >= dayStartUtc && r.ReservationDate < nextDayStartUtc);
            }

            if (query.TableId.HasValue)
            {
                reservationsQuery = reservationsQuery.Where(r => r.TableId == query.TableId.Value);
            }

            if (query.Status.HasValue)
            {
                reservationsQuery = reservationsQuery.Where(r => r.Status == query.Status.Value);
            }

            if (query.CustomerId.HasValue)
            {
                reservationsQuery = reservationsQuery.Where(r => r.CustomerId == query.CustomerId.Value);
            }

            // Get total count
            var totalCount = await reservationsQuery.CountAsync(cancellationToken);

            // Apply pagination
            var reservations = await reservationsQuery
                .OrderBy(r => r.ReservationDate)
                .ThenBy(r => r.StartTime)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(r => new ReservationDto
                {
                    Id = r.Id,
                    CustomerId = r.CustomerId,
                    CustomerName = r.CustomerName,
                    CustomerEmail = r.CustomerEmail,
                    CustomerPhone = r.CustomerPhone ?? string.Empty,
                    TableId = r.TableId,
                    TableNumber = r.Table.TableNumber,
                    ReservationDate = r.ReservationDate,
                    StartTime = r.StartTime,
                    EndTime = r.EndTime,
                    NumberOfGuests = r.NumberOfGuests,
                    Status = r.Status,
                    SpecialRequests = r.SpecialRequests,
                    Notes = r.Notes,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

            var pagedResult = new PagedResult<ReservationDto>(
                Items: reservations,
                TotalCount: totalCount,
                Page: query.Page,
                PageSize: query.PageSize,
                TotalPages: totalPages
            );

            return ApiResponse<PagedResult<ReservationDto>>.SuccessWithData(pagedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reservations");
            return ApiResponse<PagedResult<ReservationDto>>.Failure("Failed to retrieve reservations");
        }
    }
}
