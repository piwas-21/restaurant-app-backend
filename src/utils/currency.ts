/**
 * Currency formatting utilities.
 *
 * Single source of truth for monetary value formatting. Use this instead of
 * inlining `new Intl.NumberFormat(...)` per CLAUDE.md §3 (DRY).
 *
 * Default locale: de-CH (Swiss German), default currency: CHF.
 * If we ever support multiple currencies or non-Swiss locales, take the locale
 * + currency as parameters here rather than scattering Intl.NumberFormat calls.
 */

const DEFAULT_LOCALE = 'de-CH';
const DEFAULT_CURRENCY = 'CHF';

/**
 * Format a numeric amount as a currency string.
 *
 * @example
 *   formatCurrency(19.99)       // "CHF 19.99"
 *   formatCurrency(1234.5, 'fr-FR', 'EUR')  // "1 234,50 €"
 *
 * @param amount    Numeric value to format
 * @param locale    BCP-47 locale tag (default: de-CH)
 * @param currency  ISO 4217 currency code (default: CHF)
 */
export function formatCurrency(
  amount: number,
  locale: string = DEFAULT_LOCALE,
  currency: string = DEFAULT_CURRENCY,
): string {
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency,
  }).format(amount);
}
