using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed record CustomerReservationEditReadSnapshot(Guid Owner, Guid Revision, string Currency,
    CustomerReservationEditContextDto Context, IReadOnlyList<Guid> AssignedTables);

public interface ICustomerReservationEditReadAccess
{
    Task<CustomerReservationEditReadSnapshot> ReadAsync(Guid reservationId, CancellationToken cancellationToken);
    Task RevalidateAsync(CustomerReservationEditReadSnapshot original, CancellationToken cancellationToken);
}
