using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Admin-configurable visibility/requiredness of a single customer-facing form field —
/// one row per (FormKey, FieldKey) pair, mirroring the OrderTypeConfiguration
/// row-per-key pattern. The allowed pairs, their locked flags and their defaults live
/// in the API-layer FormFieldRegistry; rows are inserted from that registry on first
/// read so newly registered fields self-heal.
/// </summary>
public class CustomerFormFieldConfiguration : Entity
{
    /// <summary>Which customer form the field belongs to, e.g. "reservation".</summary>
    public string FormKey { get; set; } = string.Empty;

    /// <summary>Field identifier within the form, e.g. "customerPhone".</summary>
    public string FieldKey { get; set; } = string.Empty;

    public bool IsVisible { get; set; } = true;

    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }
}
