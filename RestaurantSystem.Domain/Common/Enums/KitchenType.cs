namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>
/// Kitchen type designation for products
/// Determines which kitchen station should prepare the item
/// </summary>
public enum KitchenType
{
    /// <summary>
    /// Not assigned to any specific kitchen
    /// </summary>
    None = 0,

    /// <summary>
    /// Front kitchen (counter/assembly area)
    /// For items like sandwiches, burgers, cold items
    /// </summary>
    FrontKitchen = 1,

    /// <summary>
    /// Back kitchen (main cooking area)
    /// For items requiring cooking/grilling
    /// </summary>
    BackKitchen = 2
}
