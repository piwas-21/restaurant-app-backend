using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class ProductImage : SoftDeleteEntity
{
    public string Url { get; set; } = null!;

    /// <summary>
    /// The card-sized WebP derivative (<see cref="RestaurantSystem.Api.Common.Services.ProductImageCardVariants"/>)
    /// of <see cref="Url"/>, when one has been generated; <c>null</c> for rows that predate the
    /// feature or whose source could not be derived. The guest card reads this; the lightbox and
    /// the admin gallery keep <see cref="Url"/>.
    /// </summary>
    public string? CardUrl { get; set; }
    public string? AltText { get; set; }
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }

    // Foreign key
    public Guid ProductId { get; set; }

    // Navigation properties
    public virtual Product Product { get; set; } = null!;
}
