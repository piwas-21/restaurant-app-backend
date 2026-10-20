using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationEditContextQuery;

public sealed record GetCustomerReservationEditContextQuery(int? SchemaVersion, Guid ReservationId)
    : IQuery<ApiResponse<CustomerReservationEditContextDto>>;

public sealed class GetCustomerReservationEditContextQueryHandler(ICustomerReservationEditReadAccess access)
    : IQueryHandler<GetCustomerReservationEditContextQuery, ApiResponse<CustomerReservationEditContextDto>>
{
    public async Task<ApiResponse<CustomerReservationEditContextDto>> Handle(GetCustomerReservationEditContextQuery query,
        CancellationToken cancellationToken) =>
        ApiResponse<CustomerReservationEditContextDto>.SuccessWithData((await access.ReadAsync(query.ReservationId, cancellationToken)).Context);
}
