using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Categories.Dtos;

public record CategoryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsActive { get; init; }
    public int DisplayOrder { get; init; }

    /// <summary>
    /// The category stays orderable on its own tab; when true its products are left out of the
    /// guest "All" list (see <c>Category.IsHiddenFromAllTab</c>). Admin-facing and informational
    /// on public clients — the filtering happens server-side.
    /// </summary>
    public bool IsHiddenFromAllTab { get; init; }
    public int ProductCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>
    /// The raw <c>OrderChannels</c> mask (<c>null</c> = every channel). The admin channel matrix
    /// writes this; products in the category inherit it unless they override.
    /// </summary>
    public int? AvailableOrderTypes { get; init; }

    /// <summary>
    /// The order types this category permits, expanded from <see cref="AvailableOrderTypes"/> so
    /// clients never decode the mask themselves. Unrestricted categories list all three.
    /// </summary>
    /// <remarks>
    /// Computed rather than assigned so each of the six <c>CategoryDto</c> projections only has to
    /// set the mask — one EF-translatable field. <c>GetCategoriesQuery</c> builds its DTO inside a
    /// server-side <c>.Select()</c> (it counts products in SQL), where a call to
    /// <c>OrderChannelMap</c> could not be translated.
    /// </remarks>
    public IReadOnlyList<OrderType> AllowedOrderTypes => OrderChannelMap.ToOrderTypes(AvailableOrderTypes);
}
