using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable, actor-bound receipt for a deterministic refusal before refund IO.</summary>
public sealed class OrderAmendmentResolutionRefusal : Entity
{
    public Guid ActorUserId { get; set; }
    public Guid SourceOrderId { get; set; }
    public Guid AmendmentId { get; set; }
    public Guid ClientOperationId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string FailureCode { get; set; } = string.Empty;
    public string OriginalRequestJson { get; set; } = string.Empty;
}
