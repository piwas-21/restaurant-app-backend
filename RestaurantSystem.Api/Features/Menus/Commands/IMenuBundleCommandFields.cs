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

    /// <summary>
    /// The bundle's own <c>OrderChannels</c> mask; <c>null</c> = inherit from the primary category
    /// (ORDER-TYPE-AVAILABILITY-PLAN §9.2). Bundles created through the admin editor carry no
    /// categories at all, so inheritance has nothing to resolve — this field is the only way a
    /// bundle's channel set can be expressed.
    /// </summary>
    int? AvailableOrderTypes { get; }

    /// <summary>
    /// The bundle's OWN allergen labelling (#478). Not derivable from its sections — a section
    /// offers alternatives, so the guest's actual plate is unknown until they choose — and
    /// <c>MenuBundleMapper</c> has served it since #477 while nothing could write it.
    /// </summary>
    /// <remarks>
    /// <b>On UPDATE, <c>null</c> means LEAVE ALONE and <c>[]</c> means CLEAR.</b> They are
    /// different instructions: a client that predates the field sends nothing and must not strip a
    /// labelled combo, while an admin who unticks every chip means it. Note this is the OPPOSITE
    /// of the same column's behaviour on <c>PUT /api/Products/{id}</c>, where
    /// <c>UpdateProductCommand</c> assigns unconditionally and null clears — one column, two
    /// endpoints, two meanings, so do not carry an assumption from one to the other.
    /// <para>
    /// On CREATE the distinction does not arise: there is nothing stored to leave alone, so the
    /// value is assigned as given and <c>null</c> simply yields an unlabelled bundle.
    /// </para>
    /// </remarks>
    List<string>? Allergens { get; }
}
