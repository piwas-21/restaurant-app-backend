using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable billing attribution for one root item unit.</summary>
public sealed class OrderBillingSnapshotUnit : Entity
{
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public int UnitOrdinal { get; set; }
    public long GrossFoodMinor { get; set; }
    public long TaxMinor { get; set; }
    public int TaxRateBasisPoints { get; set; }
    public string TaxCategory { get; set; } = "none";
    public OrderBillingTaxTreatment TaxTreatment { get; set; } = OrderBillingTaxTreatment.NotApplied;
    public long OrderDiscountMinor { get; set; }
    public long CustomerDiscountMinor { get; set; }
    public long CourtesyRoundingMinor { get; set; }
    public int RedeemedPoints { get; set; }
    public long RedemptionDiscountMinor { get; set; }
    public long PayableFoodMinor { get; set; }
    public long FoodReconciliationMinor { get; set; }
    public long EarningBasisMinor { get; set; }
    public int EarnedPoints { get; set; }
}
