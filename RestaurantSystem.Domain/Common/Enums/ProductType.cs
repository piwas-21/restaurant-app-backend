using System.Runtime.Serialization;
namespace RestaurantSystem.Domain.Common.Enums;

public enum ProductType
{
    [EnumMember(Value = "mainItem")]
    MainItem = 0,

    [EnumMember(Value = "beverage")]
    Beverage = 1,

    [EnumMember(Value = "dessert")]
    Dessert = 2,

    [EnumMember(Value = "sauce")]
    Sauce = 3,

    [EnumMember(Value = "addOn")]
    AddOn = 4,

    [EnumMember(Value = "menu")]
    Menu = 5
}
