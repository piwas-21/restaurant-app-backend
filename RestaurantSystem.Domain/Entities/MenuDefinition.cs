using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class MenuDefinition : Entity
{
    public Guid ProductId { get; set; }

    /// <summary>
    /// Optional commercial offer that this menu upgrades. The menu remains an independent Product
    /// operationally; this relation only controls catalogue presentation and authoring.
    /// </summary>
    public Guid? ParentOfferProductId { get; set; }

    /// <summary>
    /// When set, the menu applies only to this variation of <see cref="ParentOfferProductId"/>.
    /// Null applies to the parent's base offer and is also the legacy-compatible value.
    /// </summary>
    public Guid? ParentOfferVariationId { get; set; }

    // Scheduling
    public bool IsAlwaysAvailable { get; set; } = true;
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }

    // Bitmask for days of week (1=Monday, 2=Tuesday, etc. or 0-6)
    // Or simple boolean flags
    public bool AvailableMonday { get; set; } = true;
    public bool AvailableTuesday { get; set; } = true;
    public bool AvailableWednesday { get; set; } = true;
    public bool AvailableThursday { get; set; } = true;
    public bool AvailableFriday { get; set; } = true;
    public bool AvailableSaturday { get; set; } = true;
    public bool AvailableSunday { get; set; } = true;

    // Navigation
    public virtual Product Product { get; set; } = null!;
    public virtual Product? ParentOfferProduct { get; set; }
    public virtual ProductVariation? ParentOfferVariation { get; set; }
    public virtual ICollection<MenuSection> Sections { get; set; } = new List<MenuSection>();
}
