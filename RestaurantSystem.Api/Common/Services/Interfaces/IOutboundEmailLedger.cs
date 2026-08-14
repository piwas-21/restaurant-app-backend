namespace RestaurantSystem.Api.Common.Services.Interfaces;

/// <summary>
/// Claims the right to send one mail exactly once.
///
/// The rule it enforces is a database UNIQUE index, not a code path: since GAP-11 moved order mail
/// server-side, two callers (the order handler and the guest's still-open tab) can ask for the same
/// mail at the same moment, and a read-then-send would let both through.
///
/// Every method resolves its own DI scope and its own <c>ApplicationDbContext</c>. That is
/// deliberate: a claim must commit on its own, independently of whatever the caller's context
/// happens to be tracking, and the admin alert is sent from a detached task whose request scope is
/// already gone.
/// </summary>
public interface IOutboundEmailLedger
{
    /// <summary>
    /// True if the caller now owns the send; false if this mail was already sent (or is in flight
    /// elsewhere) and must be skipped. Never throws on contention.
    /// </summary>
    Task<bool> TryClaimAsync(string emailType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>Marks a claim as sent. Failures are logged, never thrown — the mail is already gone.</summary>
    Task MarkSentAsync(string emailType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops an unsent claim so a later attempt can try again. Called when the send throws; a
    /// swallowed failure that kept its claim would make the mail permanently unsendable.
    /// </summary>
    Task ReleaseAsync(string emailType, Guid entityId, CancellationToken cancellationToken = default);
}
