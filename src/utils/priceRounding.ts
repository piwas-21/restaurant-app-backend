/**
 * Utility functions for handling special price rounding logic for discounted customers
 */

/**
 * Applies special rounding for discounted prices.
 * If decimal part is less than 0.10, round down to nearest whole number.
 * If decimal part is 0.10 or greater, round up to next whole number.
 *
 * @param price - The price to round
 * @param hasDiscount - Whether the customer has an active discount
 * @returns Rounded price if customer has discount, otherwise returns original price with 2 decimal places
 */
export function applySpecialRounding(price: number, hasDiscount: boolean): number {
  if (!hasDiscount) {
    // No special rounding for regular customers
    return Math.round(price * 100) / 100; // Round to 2 decimal places
  }

  // Get the fractional part
  const fractionalPart = price - Math.floor(price);

  if (fractionalPart < 0.10) {
    // Round down to nearest whole number
    return Math.floor(price);
  } else {
    // Round up to next whole number
    return Math.ceil(price);
  }
}

/**
 * Checks if a customer has any active discount
 *
 * @param discountAmount - The calculated discount amount
 * @returns True if there is an active discount, false otherwise
 */
export function hasActiveDiscount(discountAmount: number): boolean {
  return discountAmount > 0;
}

/**
 * Formats price with special rounding for discounted customers
 *
 * @param price - The price to format
 * @param hasDiscount - Whether the customer has an active discount
 * @returns Formatted price string with appropriate decimal places
 */
export function formatPriceWithRounding(price: number, hasDiscount: boolean): string {
  const roundedPrice = applySpecialRounding(price, hasDiscount);

  if (hasDiscount) {
    // Discounted prices are whole numbers
    return roundedPrice.toFixed(0);
  } else {
    // Regular prices show 2 decimal places
    return roundedPrice.toFixed(2);
  }
}
