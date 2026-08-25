namespace RestaurantSystem.Api.Common.Templates;

/// <summary>
/// What a guest's own edit of their booking left behind — the one sentence the "your reservation
/// was updated" mail (M16) has to get right.
/// </summary>
/// <remarks>
/// An enum and not a <c>bool</c> because there are three honest answers, and the two "waiting"
/// ones are not the same news: a booking that was CONFIRMED and lost that confirmation has to say
/// so plainly (the guest holds a mail that says the table is theirs), while a booking that was
/// pending all along has simply not been decided yet. Collapsing them would either alarm a guest
/// whose booking never changed state, or quietly drop the fact that an approval was withdrawn.
/// </remarks>
public enum ReservationChangeOutcome
{
    /// <summary>Contact details only: the restaurant's decision still stands.</summary>
    StillConfirmed,

    /// <summary>Was already pending, still pending — the wait continues with new numbers.</summary>
    AwaitingApproval,

    /// <summary>Was confirmed; the shape changed, so the confirmation was withdrawn.</summary>
    NeedsApprovalAgain
}
