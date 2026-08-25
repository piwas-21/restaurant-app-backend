namespace RestaurantSystem.Api.Common.Templates;

/// <summary>
/// The booking a guest's edit replaced — what the restaurant had in front of it when it last
/// decided, and the half of the changed-booking alert (M17) that makes it readable at a glance.
/// </summary>
/// <remarks>
/// Only the four fields a guest can reshape, plus the state the edit found: the table never moves
/// on that route, and the contact details are printed from the CURRENT values, which is what the
/// restaurant would phone. <paramref name="WasConfirmed"/> is carried rather than derived, because
/// by the time a mail is rendered the reservation is already back to <c>Pending</c> and the fact
/// that an approval was withdrawn is no longer visible anywhere on the row.
/// </remarks>
/// <param name="Date">The calendar day previously booked, on the RESTAURANT's clock.</param>
/// <param name="StartTime">Previous start of the sitting, wall clock.</param>
/// <param name="EndTime">Previous end of the sitting, wall clock.</param>
/// <param name="NumberOfGuests">Previous party size.</param>
/// <param name="WasConfirmed">Whether the restaurant had already approved this booking.</param>
public readonly record struct ReservationPreviousBooking(
    DateTime Date,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int NumberOfGuests,
    bool WasConfirmed);
