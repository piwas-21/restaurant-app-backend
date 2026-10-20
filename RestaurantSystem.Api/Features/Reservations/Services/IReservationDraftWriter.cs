using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed record ReservationCreationDraft(Reservation? Reservation, string? TableNumber, string? Error)
{
    public static ReservationCreationDraft Refused(string error) => new(null, null, error);
}

/// <summary>Stages the shared booking rules in the caller's schedule transaction; never commits or mails.</summary>
public interface IReservationDraftWriter
{
    Task<ReservationCreationDraft> CreateUnderLockAsync(CreateReservationDto data, Guid? customerId,
        string? language, CancellationToken cancellationToken);
}
