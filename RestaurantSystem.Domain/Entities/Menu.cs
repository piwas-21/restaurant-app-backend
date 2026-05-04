using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class Menu : SoftDeleteEntity
{
    public string Name { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSpecial { get; set; } = false; // Is this a special menu (e.g., holiday menu)
    public int DisplayOrder { get; set; } = 0;
    public decimal BasePrice { get; set; } // Override regular price if set

    // Navigation properties
    public virtual ICollection<MenuItem> MenuItems { get; set; } = [];
}
