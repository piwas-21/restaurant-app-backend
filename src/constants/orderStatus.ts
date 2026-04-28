import type { OrderStatus } from '@/types/order';

export const ACTIVE_STATUSES: OrderStatus[] = [
  'Pending',
  'PendingApproval',
  'Confirmed',
  'Preparing',
  'In Progress',
  'Ready',
  'InTransit', // same concept as OutForDelivery in the spec
];

// 'Refunded' is a PaymentStatus, not an OrderStatus — not listed here intentionally.
export const PAST_STATUSES: OrderStatus[] = ['Delivered', 'Completed', 'Cancelled'];
