/**
 * Order Error Handler Utility
 *
 * Maps backend error messages to translated i18n keys
 */

import { TFunction } from 'i18next';
import { ApiError, getErrorMessage } from '@/utils/apiClient';

/**
 * Map backend error messages to translated error keys
 *
 * @param error - The error object from the API
 * @param t - Translation function from react-i18next
 * @returns Translated error message
 */
export function getTranslatedOrderError(error: unknown, t: TFunction): string {
  // Get the raw error message
  const rawMessage = getErrorMessage(error);

  // Map common backend error messages to translation keys
  const errorMappings: Record<string, string> = {
    'Delivery address is required': 'error_delivery_address_required',
    'delivery address is required': 'error_delivery_address_required',
    'Product not found': 'error_product_unavailable',
    'product not found': 'error_product_unavailable',
    'Invalid table number': 'error_invalid_table_number',
    'invalid table number': 'error_invalid_table_number',
    'Payment amount': 'error_payment_amount_mismatch',
    'payment amount': 'error_payment_amount_mismatch',
    'Basket not found': 'error_basket_not_found',
    'basket not found': 'error_basket_not_found',
    'out of stock': 'error_insufficient_stock',
    'Out of stock': 'error_insufficient_stock',
    'Order type': 'error_order_type_required',
    'order type': 'error_order_type_required',
  };

  // Check if error message contains any of the known patterns
  const lowerMessage = rawMessage.toLowerCase();
  for (const [pattern, key] of Object.entries(errorMappings)) {
    if (lowerMessage.includes(pattern.toLowerCase())) {
      return t(key, rawMessage);
    }
  }

  // If no specific mapping found, return generic error or original message
  if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
    // Client error - show the actual message (might be validation error)
    return rawMessage;
  }

  // Server error or unknown - show generic message
  return t('error_unexpected', 'An unexpected error occurred. Please try again.');
}
