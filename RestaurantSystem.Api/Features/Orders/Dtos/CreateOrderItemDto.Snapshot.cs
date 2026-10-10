using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public partial record CreateOrderItemDto
{
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
