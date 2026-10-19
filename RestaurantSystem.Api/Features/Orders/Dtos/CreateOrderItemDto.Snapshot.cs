using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public partial record CreateOrderItemDto
{
    // Server-owned presentation from a persisted basket, keyed by exact customization membership.
    internal IReadOnlyDictionary<Guid, CreateOrderItemDto>? CustomizationOptionPresentation { get; set; }
    internal Guid? ParentComponentMenuSectionItemId { get; init; }
    internal Guid? SuggestedSideItemId { get; init; }
    internal QuantityBasis? QuantityBasis { get; init; }
    internal ConfigurationScope? ConfigurationScope { get; init; }
    internal CompositionRole? CompositionRole { get; init; }
    internal QuantityBasis? IngredientQuantityBasis { get; init; }
    internal ConfigurationScope? IngredientConfigurationScope { get; init; }
    internal Dictionary<Guid, CompositionRole>? IngredientCompositionRoles { get; init; }
    internal string? PresentationLabel { get; init; }
    internal int? PresentationOrder { get; init; }
}
