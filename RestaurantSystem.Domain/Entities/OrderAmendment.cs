using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Durable quote, immutable source/supplement snapshots, commit replay key and resolution seam for
/// one staff amendment. Original order rows and captured tenders remain unchanged.
/// </summary>
public sealed class OrderAmendment : Entity
{
    public Guid SourceOrderId { get; set; }
    public Guid? ServiceSessionId { get; set; }
    public Guid? SupplementOrderId { get; set; }
    public Guid? ClientOperationId { get; set; }
    public Guid ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public OrderAmendmentState State { get; set; } = OrderAmendmentState.Quoted;
    public string PayloadHash { get; set; } = string.Empty;
    public string? CommitPayloadHash { get; set; }
    public int ExpectedOrderVersion { get; set; }
    public long? ExpectedAccountRevision { get; set; }
    public long? CommittedAccountRevision { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? CommittedAt { get; set; }

    /// <summary>Canonical request accepted for the quote; commit never trusts a resubmitted basket.</summary>
    public string RequestJson { get; set; } = string.Empty;

    /// <summary>Frozen source line snapshots and one-based ordinal scopes at quote time.</summary>
    public string ChangesJson { get; set; } = "[]";

    /// <summary>Immutable source order snapshot that was reviewed by the staff member.</summary>
    public string SourceSnapshotJson { get; set; } = string.Empty;

    /// <summary>Immutable priced supplement snapshot; null when the amendment adds no new items.</summary>
    public string? SupplementSnapshotJson { get; set; }

    /// <summary>Minor-unit preview plus explicit credit, loyalty and refund resolution states.</summary>
    public string FinancialResolutionJson { get; set; } = string.Empty;

    /// <summary>Frozen successful response returned for every retry of this commit operation.</summary>
    public string? CommitResultJson { get; set; }
}
