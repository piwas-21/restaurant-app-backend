using RestaurantSystem.Api.Features.Products.Dtos;

namespace RestaurantSystem.Api.Features.Menus.Commands;

/// <summary>
/// The create/update menu-bundle command fields that share validation rules. Implemented by both
/// commands so <see cref="MenuBundleCommandValidatorBase{T}"/> can hold the common rules once
/// (menu-bundles redesign #156).
/// </summary>
public interface IMenuBundleCommandFields
{
    string Name { get; }
    string? Description { get; }
    decimal BasePrice { get; }
    int PreparationTimeMinutes { get; }
    int DisplayOrder { get; }
    List<Guid>? CategoryIds { get; }
    Guid? PrimaryCategoryId { get; }
    MenuDefinitionDto MenuDefinition { get; }
}
