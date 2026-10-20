using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>Which of the two decisions a quick-action link carries. Part of what the token signs.</summary>
public enum ReservationQuickAction
{
    /// <summary>Confirm the booking (<c>quick-approve</c>).</summary>
    Approve,

    /// <summary>Turn the booking down (<c>quick-reject</c>).</summary>
    Reject,
}

/// <summary>What a caller's link proved.</summary>
public enum QuickActionLinkVerdict
{
    /// <summary>No usable proof — missing, malformed, wrong, expired, or already spent. Refuse.</summary>
    Refused,

    /// <summary>A valid signature over this reservation, this action and its CURRENT status and database revision.</summary>
    SignatureValid,

    /// <summary>
    /// Retained source compatibility value; revisionless legacy links no longer grant authority.
    /// </summary>
    Legacy,
}

/// <summary>
/// Mints and checks the signature on the anonymous quick-approve / quick-reject links in the
/// restaurant's reservation alert mail (backend #402).
/// </summary>
/// <remarks>
/// An interface rather than a static helper — unlike its <c>QuickActionTokens</c> cousin for
/// orders — because this one has a key, a clock and two configurable windows behind it, all of
/// which a test has to be able to set.
/// </remarks>
public interface IReservationQuickActionLinks
{
    /// <summary>
    /// Binds the action to the actual saved booking revision and status. A later update permanently retires it.
    /// </summary>
    string Mint(Guid reservationId, ReservationQuickAction action, ReservationStatus status, Guid revision);

    /// <summary>
    /// Decides whether a request may act. Never throws, never tells the caller which of the many
    /// reasons applied — the controller renders one page for every <see cref="QuickActionLinkVerdict.Refused"/>.
    /// </summary>
    /// <param name="currentStatus">The reservation's status as stored, not as the link claims.</param>
    /// <param name="currentRevision">Database-owned current revision; never a client claim.</param>
    /// <param name="token">The <c>?token=</c> query value. Missing or revisionless proof is refused.</param>
    QuickActionLinkVerdict Verify(
        Guid reservationId,
        ReservationQuickAction action,
        ReservationStatus currentStatus,
        Guid currentRevision,
        string? token);
}
