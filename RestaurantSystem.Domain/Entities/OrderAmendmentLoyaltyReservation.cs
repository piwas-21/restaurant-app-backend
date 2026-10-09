using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public enum OrderAmendmentLoyaltyReservationState
{
    HeldShortfall = 1,
    Reserved = 2,
    Consumed = 3,
    Released = 4
}

/// <summary>Mutable operation state for an immutable earned-points clawback obligation.</summary>
public sealed class OrderAmendmentLoyaltyReservation : Entity
{
    public Guid SourceOrderId { get; set; }
    public Guid OperationId { get; set; }
    public Guid CompensationId { get; set; }
    public Guid OwnerLinkId { get; set; }
    public OrderAmendmentLoyaltyReservationState State { get; set; }
}
