namespace RestaurantSystem.Domain.Entities;

public partial class Order
{
    /// <summary>Materialized append-only amendment credits; the original charge remains in Total.</summary>
    public decimal BillingCreditAmount { get; set; }

    public decimal PayableTotal => Total - BillingCreditAmount;
}
