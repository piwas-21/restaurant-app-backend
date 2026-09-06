using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class Category : SoftDeleteEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Partner request 2026-09-06 (MC FOOD): a category the owner wants orderable on its own tab
    /// but OUT of the combined guest "All" view (internal groups like VIANDE/BOULGOUR whose items
    /// duplicate their sandwich entries). Read ONLY by the guest all-view filter in
    /// <c>GetProductsQuery</c> — the category tab, its nav entry and staff views are untouched,
    /// so "hide" means exactly and only "not in the guest All list".
    /// </summary>
    public bool IsHiddenFromAllTab { get; set; }

    /// <summary>
    /// The <see cref="Common.Enums.OrderChannels"/> bitmask this category may be ordered through.
    /// <c>null</c> = every channel. Products in this category inherit it unless they override.
    /// Always read via <see cref="Common.OrderChannelMap"/> — never cast.
    /// </summary>
    public int? AvailableOrderTypes { get; set; }

    public virtual ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();
}
