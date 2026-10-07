using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Retains an opaque owner link while amendment loyalty obligations remain unsettled.</summary>
public sealed class OrderAmendmentLoyaltyOwnerHold : Entity
{
    public Guid SourceOrderId { get; set; }
    public Guid OperationId { get; set; }
    public Guid OwnerLinkId { get; set; }
    public DateTime? ReleasedAt { get; set; }
}
