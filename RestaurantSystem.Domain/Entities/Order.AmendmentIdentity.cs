namespace RestaurantSystem.Domain.Entities;

public partial class Order
{
    /// <summary>Original order for every amendment dispatch, including amendments of supplements.</summary>
    public Guid? AmendmentRootOrderId { get; set; }
}
