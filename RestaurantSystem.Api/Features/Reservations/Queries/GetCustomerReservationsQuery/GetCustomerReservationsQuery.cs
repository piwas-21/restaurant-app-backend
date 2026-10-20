using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationsQuery;

public sealed record GetCustomerReservationsQuery(int? SchemaVersion, DateTime? BeforeDate = null,
    TimeSpan? BeforeStartTime = null, Guid? BeforeId = null) : IQuery<ApiResponse<CustomerReservationsPageDto>>;

public sealed class GetCustomerReservationsQueryHandler(ApplicationDbContext context, CustomMediator mediator,
    ICurrentUserService current, ITenantClock clock)
    : IQueryHandler<GetCustomerReservationsQuery, ApiResponse<CustomerReservationsPageDto>>
{
    private const int PageSize = 20;
    public async Task<ApiResponse<CustomerReservationsPageDto>> Handle(GetCustomerReservationsQuery query,
        CancellationToken cancellationToken)
    {
        var owner = await CustomerReservationReadAccess.RequireEnabledAsync(mediator, current, cancellationToken);
        var rows = CustomerReservationReadAccess.Scope(context, owner);
        if (query.BeforeDate is { } date && query.BeforeStartTime is { } start && query.BeforeId is { } id)
        {
            rows = rows.Where(row => row.ReservationDate < date
                || row.ReservationDate == date && row.StartTime > start
                || row.ReservationDate == date && row.StartTime == start && row.Id.CompareTo(id) > 0);
        }
        var found = await CustomerReservationProjection.Select(rows.OrderByDescending(row => row.ReservationDate)
            .ThenBy(row => row.StartTime).ThenBy(row => row.Id).Take(PageSize + 1)).ToArrayAsync(cancellationToken);
        await CustomerReservationReadAccess.RequireCurrentAsync(context, owner, cancellationToken);
        await CustomerReservationReadAccess.RequireEnabledAsync(mediator, current, cancellationToken);
        var items = found.Take(PageSize).Select(CustomerReservationProjection.ToDto).ToArray();
        // Keep the exact database sort marker. Older writes may include a non-midnight time;
        // truncating the cursor to the displayed calendar day would skip later pages.
        var last = found.Take(PageSize).LastOrDefault();
        CustomerReservationCursorDto? next = found.Length > PageSize && last is not null
            ? new(last.Date, last.Start, last.Id) : null;
        return ApiResponse<CustomerReservationsPageDto>.SuccessWithData(new(1, clock.TimeZone.Id, items, next));
    }
}
