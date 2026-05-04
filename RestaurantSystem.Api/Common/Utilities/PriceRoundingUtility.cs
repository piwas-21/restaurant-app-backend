namespace RestaurantSystem.Api.Common.Utilities;

/// <summary>
/// Utility class for handling special price rounding logic for discounted customers
/// </summary>
public static class PriceRoundingUtility
{
    /// <summary>
    /// Applies special rounding for discounted prices.
    /// If decimal part is less than 0.10, round down to nearest whole number.
    /// If decimal part is 0.10 or greater, round up to next whole number.
    /// </summary>
    /// <param name="price">The price to round</param>
    /// <param name="hasDiscount">Whether the customer has an active discount</param>
    /// <returns>Rounded price if customer has discount, otherwise returns original price with 2 decimal places</returns>
    public static decimal ApplySpecialRounding(decimal price, bool hasDiscount)
    {
        if (!hasDiscount)
        {
            // No special rounding for regular customers
            return Math.Round(price, 2);
        }

        // Get the fractional part
        decimal fractionalPart = price - Math.Floor(price);

        if (fractionalPart < 0.10m)
        {
            // Round down to nearest whole number
            return Math.Floor(price);
        }
        else
        {
            // Round up to next whole number
            return Math.Ceiling(price);
        }
    }

    /// <summary>
    /// Checks if a user has any active discount
    /// </summary>
    /// <param name="discountAmount">The calculated discount amount</param>
    /// <returns>True if there is an active discount, false otherwise</returns>
    public static bool HasActiveDiscount(decimal discountAmount)
    {
        return discountAmount > 0;
    }
}
