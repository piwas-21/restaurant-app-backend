using System.Runtime.Serialization;

namespace RestaurantSystem.Domain.Common.Enums;

public enum OptionSetKind
{
    [EnumMember(Value = "ingredient")]
    Ingredient = 0,
    [EnumMember(Value = "sauce")]
    Sauce = 1,
    [EnumMember(Value = "bundleChoice")]
    BundleChoice = 2,
    [EnumMember(Value = "suggestedSide")]
    SuggestedSide = 3
}
