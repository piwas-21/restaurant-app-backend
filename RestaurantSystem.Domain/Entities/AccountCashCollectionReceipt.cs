using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable physical cash received for one captured table-account contribution.</summary>
public sealed class AccountCashCollectionReceipt : Entity
{
    public Guid AttemptId { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    public long ExactAmountMinor { get; set; }
    public long AdjustmentMinor { get; set; }
    public long DueAmountMinor { get; set; }
    public long ReceivedMinor { get; set; }
    public long ChangeMinor { get; set; }
    public long ExpectedAccountRevision { get; set; }
    public int ExpectedVersion { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public Guid ActorId { get; set; }
    public AccountPaymentActorKind ActorKind { get; set; }
    public UserRole ActorRole { get; set; }
    public DateTime CapturedAt { get; set; }
    public AccountPaymentAttempt? Attempt { get; set; }
}
