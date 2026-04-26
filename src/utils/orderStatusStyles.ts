import styles from '@/styles/orderStatus.module.css';
import { OrderStatus } from '@/types/order';

/**
 * Get the CSS class name for an order status badge
 */
export const getOrderStatusClass = (status: string): string => {
  const statusLower = status.toLowerCase();

  switch (statusLower) {
    case 'pending':
      return styles.statusPending;
    case 'confirmed':
      return styles.statusConfirmed;
    case 'preparing':
      return styles.statusPreparing;
    case 'ready':
      return styles.statusReady;
    case 'intransit':
    case 'in transit':
      return styles.statusInTransit;
    case 'delivered':
      return styles.statusDelivered;
    case 'completed':
      return styles.statusCompleted;
    case 'cancelled':
      return styles.statusCancelled;
    default:
      return styles.statusPending;
  }
};

/**
 * Get the CSS class name for a payment status badge
 */
export const getPaymentStatusClass = (status: string): string => {
  const statusLower = status.toLowerCase();

  switch (statusLower) {
    case 'pending':
      return styles.paymentPending;
    case 'paid':
      return styles.paymentPaid;
    case 'partiallypaid':
    case 'partially paid':
      return styles.paymentPartiallyPaid;
    case 'refunded':
      return styles.paymentRefunded;
    case 'failed':
      return styles.paymentFailed;
    default:
      return styles.paymentPending;
  }
};

/**
 * Get combined class names for status badge
 */
export const getStatusBadgeClasses = (status: string): string => {
  return `${styles.statusBadge} ${getOrderStatusClass(status)}`;
};

/**
 * Get combined class names for payment badge
 */
export const getPaymentBadgeClasses = (status: string): string => {
  return `${styles.paymentBadge} ${getPaymentStatusClass(status)}`;
};

/**
 * Get focus badge class
 */
export const getFocusBadgeClass = (): string => {
  return styles.focusBadge;
};

export const getOrderStatusTranslationKey = (status: OrderStatus): string => {
    switch (status) {
      case "Pending": return 'order_status_pending';
      case "Confirmed": return 'order_status_confirmed';
      case "Preparing": return 'order_status_preparing';
      case "Ready": return 'order_status_ready';
      case "InTransit": return 'order_status_intransit';
      case "Delivered": return 'order_status_delivered';
      case "Completed": return 'order_status_completed';
      case "Cancelled": return 'order_status_cancelled';
      case "PendingApproval": return 'order_status_pending_approval';
      default: return status;
    }
  };
