using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>Original email proof, carried only by trusted in-process dispatch. Validation before
/// waiting is insufficient: status and expiry must still authorize the fresh locked booking.</summary>
internal sealed class ReservationQuickActionAuthority(string? token)
{
    internal const string RefusalCode = "ReservationQuickActionUnavailable";

    internal bool Allows(Reservation? reservation, ReservationQuickAction action, IReservationQuickActionLinks links) =>
        reservation is not null && links.Verify(reservation.Id, action, reservation.Status,
            reservation.CreatedAt, token) != QuickActionLinkVerdict.Refused;

    internal static ApiResponse<bool> Refused() =>
        ApiResponse<bool>.FailureWithCode("Reservation link unavailable.", RefusalCode);
}
