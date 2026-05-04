using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

public class TaxConfiguration : Entity
{
    public string Name { get; set; } = string.Empty; // e.g., "VAT", "Sales Tax"
    public decimal Rate { get; set; } // e.g., 0.08 for 8%
    public bool IsEnabled { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ApplicableOrderTypes { get; set; } = string.Empty; // Comma-separated OrderType values (e.g., "1,2" for DineIn,Takeaway)
}
