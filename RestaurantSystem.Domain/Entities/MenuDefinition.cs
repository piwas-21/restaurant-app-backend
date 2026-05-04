using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class MenuDefinition : Entity
{
    public Guid ProductId { get; set; }

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
    public virtual ICollection<MenuSection> Sections { get; set; } = new List<MenuSection>();
}
