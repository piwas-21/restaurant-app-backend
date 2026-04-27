/**
 * Email Service
 *
 * Service for sending emails via the backend API
 */

import { apiClient } from '@/utils/apiClient';

/**
 * Send order confirmation emails to both customer and admin
 * This sends a single API request that handles both customer and admin emails
 *
 * @param orderId - Order ID
 * @returns Promise that resolves when emails are sent
 */
export async function sendOrderConfirmationEmails(orderId: string): Promise<void> {
  try {
    const response = await apiClient.post<{ data: string }>(
      `/api/Orders/${orderId}/send-confirmation-email`,
      {},
      { requireAuth: false },
    );
  } catch (error) {
    console.error('Error sending order confirmation emails:', error);
    // Don't throw - email sending should not block the order process
  }
}
