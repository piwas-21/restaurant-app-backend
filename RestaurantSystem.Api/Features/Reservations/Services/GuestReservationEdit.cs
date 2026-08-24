using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>
/// What a guest's edit of their OWN booking did to it: the booking as the restaurant last saw it,
/// plus the two facts every consequence of the edit hangs on.
/// </summary>
/// <param name="PreviousDate">The calendar day the booking held before the edit.</param>
/// <param name="PreviousStartTime">The start of the sitting before the edit.</param>
/// <param name="PreviousEndTime">The end of the sitting before the edit.</param>
/// <param name="PreviousGuests">The party size before the edit.</param>
/// <param name="WasConfirmed">Whether the restaurant had already approved the booking.</param>
/// <param name="ShapeChanged">
/// Whether the day, the hours or the party size moved. This — and NOT "something changed" — is
/// what a restaurant has to re-decide; a corrected phone number is not a new booking.
/// </param>
public readonly record struct ReservationEdit(
    DateTime PreviousDate,
    TimeSpan PreviousStartTime,
    TimeSpan PreviousEndTime,
    int PreviousGuests,
    bool WasConfirmed,
    bool ShapeChanged);

/// <summary>
/// The rules of the guest-owned edit route, in one place: what may still be edited, and what
/// writing the guest's values does to the booking's status.
/// </summary>
/// <remarks>
/// Static like <c>ReservationDtoMapper</c>, not a DI service: these are pure decisions over an
/// entity, they touch nothing else, and the handler that used to hold them sat at its 200-LOC
/// limit (CLAUDE.md §4) — the same reason <c>IReservationCreatedMailer</c> was extracted from the
/// create handler. Keeping them together also keeps the answer to "did this need a new decision?"
/// beside the code that causes it, instead of recomputed by whoever sends the mail.
/// </remarks>
public static class GuestReservationEdit
{
    /// <summary>Only a live, still-future booking is guest-editable: the cancel path's
    /// terminal-status rule plus the create path's day comparison — not a second time model.</summary>
    public static void EnsureEditable(Reservation reservation, DateTime tenantToday)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        if (reservation.Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
        {
            throw new BadRequestException(
                $"A {reservation.Status.ToString().ToLowerInvariant()} reservation can no longer be changed",
                ErrorCodes.ReservationNotEditable);
        }

        if (reservation.ReservationDate.Date < tenantToday)
        {
            throw new BadRequestException(
                "A past reservation can no longer be changed", ErrorCodes.ReservationNotEditable);
        }
    }

    /// <summary>Writes the guest-editable fields only, and reports what that did. A Confirmed
    /// booking whose SHAPE changed (day, hours or party size) drops back to Pending — the restaurant
    /// approved those numbers, not the new ones. A contact-detail-only edit keeps the
    /// confirmation.</summary>
    public static ReservationEdit Apply(
        Reservation reservation, UpdateMyReservationDto data, DateTime bookedDay)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(data);

        var edit = new ReservationEdit(
            reservation.ReservationDate,
            reservation.StartTime,
            reservation.EndTime,
            reservation.NumberOfGuests,
            reservation.Status == ReservationStatus.Confirmed,
            ShapeChanged:
                reservation.ReservationDate.Date != bookedDay.Date ||
                reservation.StartTime != data.StartTime ||
                reservation.EndTime != data.EndTime ||
                reservation.NumberOfGuests != data.NumberOfGuests);

        reservation.CustomerName = data.CustomerName;
        reservation.CustomerEmail = data.CustomerEmail;
        reservation.CustomerPhone = data.CustomerPhone;
        reservation.ReservationDate = bookedDay;
        reservation.StartTime = data.StartTime;
        reservation.EndTime = data.EndTime;
        reservation.NumberOfGuests = data.NumberOfGuests;
        reservation.SpecialRequests = data.SpecialRequests;

        if (edit.ShapeChanged && edit.WasConfirmed)
        {
            reservation.Status = ReservationStatus.Pending;
        }

        return edit;
    }
}
