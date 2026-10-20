using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationQuery;

public sealed record GetCustomerReservationQuery(int? SchemaVersion, Guid ReservationId)
    : IQuery<ApiResponse<CustomerReservationItemDto>>;

public sealed class GetCustomerReservationQueryHandler(ApplicationDbContext context, CustomMediator mediator,
    ICurrentUserService current) : IQueryHandler<GetCustomerReservationQuery, ApiResponse<CustomerReservationItemDto>>
{
    public async Task<ApiResponse<CustomerReservationItemDto>> Handle(GetCustomerReservationQuery query,
        CancellationToken cancellationToken)
    {
        var owner = await CustomerReservationReadAccess.RequireEnabledAsync(mediator, current, cancellationToken);
        var row = await CustomerReservationProjection.Select(CustomerReservationReadAccess.Scope(context, owner)
            .Where(row => row.Id == query.ReservationId)).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Reservation unavailable.");
        await CustomerReservationReadAccess.RequireCurrentAsync(context, owner, cancellationToken);
        await CustomerReservationReadAccess.RequireEnabledAsync(mediator, current, cancellationToken);
        return ApiResponse<CustomerReservationItemDto>.SuccessWithData(CustomerReservationProjection.ToDto(row));
    }
}
