using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// One claim on one outbound mail: "this mail, for this entity, is being sent — or has been".
///
/// It exists because the mails it guards stopped being triggered by the guest's browser
/// (EMAIL-SPEC-TENANT-APP GAP-11) and are now sent by the server the moment the order is
/// committed. The old client call still exists and is still reachable, so the same mail can be
/// asked for twice — once by the server, once by a tab that is still open or a replayed request.
/// The UNIQUE index on (EmailType, EntityId) is what makes "send at most once" true; a read-then-
/// send in application code would not, because both callers can read at the same instant.
///
/// A row is written BEFORE the send and marked <see cref="SentAt"/> after it. A send that throws
/// deletes its own claim, so the next attempt — the client's call, a support-triggered resend — can
/// still get the mail out. A process that dies mid-send leaves an unmarked claim behind; that is
/// what the staleness takeover in <c>OutboundEmailLedger</c> is for.
/// </summary>
public class OutboundEmail : Entity
{
    /// <summary>One of <see cref="OutboundEmailTypes"/>. Part of the uniqueness key.</summary>
    public required string EmailType { get; set; }

    /// <summary>The order, reservation or user the mail is about. Part of the uniqueness key.</summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Null while the send is in flight, set when the provider accepted it. Deliberately not a
    /// delivery confirmation — that needs the provider webhook (GAP-6), which does not exist yet.
    /// </summary>
    public DateTime? SentAt { get; set; }
}
