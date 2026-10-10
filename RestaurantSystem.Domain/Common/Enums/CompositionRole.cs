namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Frozen operational role for a selected row; never inferred from translated names.</summary>
public enum CompositionRole
{
    Menu,
    Dish,
    RequiredChoice,
    Extra,
    Sauce,
    Side,
    Drink,
    Ingredient,
    Unknown
}
