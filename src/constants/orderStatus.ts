import type { OrderStatus } from '@/types/order';

export const ACTIVE_STATUSES: OrderStatus[] = [
  'Pending',
  'PendingApproval',
  'Confirmed',
  'Preparing',
  'Ready',
  'InTransit',
];

export const PAST_STATUSES: OrderStatus[] = ['Delivered', 'Completed', 'Cancelled'];
