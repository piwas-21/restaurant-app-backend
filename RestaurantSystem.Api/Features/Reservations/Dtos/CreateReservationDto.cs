namespace RestaurantSystem.Api.Features.Reservations.Dtos;

/// <summary>
/// A guest booking. Exactly <see cref="ReservationWriteDto"/> plus the combined-tables extension
/// (#561): saying the base out loud by inheriting is the point — the two write DTOs used to be one
/// declaration written twice.
/// </summary>
public record CreateReservationDto : ReservationWriteDto
{
    /// <summary>
    /// The additional tables this booking occupies beyond <see cref="ReservationWriteDto.TableId"/>:
    /// ONE reservation over N tables (#561). Optional; the party is validated against the SUM of
    /// every table's capacity, so individual tables may each be smaller than the party. Must be
    /// distinct and must not repeat <c>TableId</c>; every table must exist and be active. The edit
    /// paths cannot change this set — an admin who needs to rearrange a combined booking recreates it.
    /// </summary>
    public List<Guid>? CombinedTableIds { get; set; }
}
