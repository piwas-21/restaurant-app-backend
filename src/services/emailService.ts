/**
 * Email Service
 *
 * Service for sending emails via the backend API
 */

import { apiClient } from '@/utils/apiClient';
import { OrderDto } from '@/types/order';

export interface SendOrderConfirmationEmailRequest {
  orderId: string;
  customerEmail: string;
  customerName: string;
  orderNumber: string;
  orderDetails: OrderDto;
  recipientType: 'customer' | 'admin';
}

/**
 * Send order confirmation email to customer
 *
 * @param orderId - Order ID
 * @param customerEmail - Customer email address
 * @param customerName - Customer name
 * @param orderNumber - Order number
 * @param orderDetails - Full order details
 * @returns Promise that resolves when email is sent
 */
export async function sendOrderConfirmationEmailToCustomer(
  orderId: string,
  customerEmail: string,
  customerName: string,
  orderNumber: string,
  orderDetails: OrderDto
): Promise<void> {
  try {
    await apiClient.post<void>(
      '/api/Emails/send-order-confirmation',
      {
        orderId,
        customerEmail,
        customerName,
        orderNumber,
        recipientType: 'customer',
      },
      { requireAuth: false }
    );
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error sending customer confirmation email:', error);
    // Don't throw - email sending should not block the order process
  }
}

/**
 * Send order confirmation email to admin
 *
 * @param orderId - Order ID
 * @param orderNumber - Order number
 * @param orderDetails - Full order details
 * @returns Promise that resolves when email is sent
 */
export async function sendOrderConfirmationEmailToAdmin(
  orderId: string,
  orderNumber: string,
  orderDetails: OrderDto
): Promise<void> {
  try {
    await apiClient.post<void>(
      '/api/Emails/send-order-confirmation-admin',
      {
        orderId,
        orderNumber,
        recipientType: 'admin',
      },
      { requireAuth: false }
    );
  } catch (error) {
    // eslint-disable-next-line no-console
    console.error('Error sending admin confirmation email:', error);
    // Don't throw - email sending should not block the order process
  }
}

/**
 * Send order confirmation emails to both customer and admin
 *
 * @param orderId - Order ID
 * @param customerEmail - Customer email address
 * @param customerName - Customer name
 * @param orderNumber - Order number
 * @param orderDetails - Full order details
 * @returns Promise that resolves when both emails are sent
 */
export async function sendOrderConfirmationEmails(
  orderId: string,
  customerEmail: string,
  customerName: string,
  orderNumber: string,
  orderDetails: OrderDto
): Promise<void> {
  // Send both emails in parallel, but don't fail if one fails
  await Promise.all([
    sendOrderConfirmationEmailToCustomer(orderId, customerEmail, customerName, orderNumber, orderDetails),
    sendOrderConfirmationEmailToAdmin(orderId, orderNumber, orderDetails),
  ]);
}
