using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class MenuSection : Entity
{
    public Guid MenuDefinitionId { get; set; }
    public string Name { get; set; } = null!; // e.g. "Choose Drink", "Main Course"
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }

    public bool IsRequired { get; set; } = true;
    public int MinSelection { get; set; } = 1;
    public int MaxSelection { get; set; } = 1; // 1 for single choice

    // Navigation
    public virtual MenuDefinition MenuDefinition { get; set; } = null!;
    public virtual ICollection<MenuSectionItem> Items { get; set; } = new List<MenuSectionItem>();
}
