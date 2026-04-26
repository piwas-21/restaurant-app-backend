'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from '../../app/styles/CashierPage.module.css';
import { OrderDto, OrderStatus } from '@/types/order';
import { OrderType } from '@/types/order';
import { getOrderStatusTranslationKey } from '@/utils/orderStatusStyles';

interface OrderListProps {
  orders: OrderDto[];
  selectedOrderId: string | null;
  onSelectOrder: (orderId: string) => void;
  isLoading: boolean;
  error: string | null;
}

// Helper to get time ago string
const getTimeAgo = (dateString: string): string => {
  const date = new Date(dateString);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffMins = Math.floor(diffMs / 60000);
  const diffHours = Math.floor(diffMins / 60);

  if (diffMins < 1) return 'Just now';
  if (diffMins < 60) return `${diffMins} min ago`;
  if (diffHours < 24) return `${diffHours} hour${diffHours > 1 ? 's' : ''} ago`;
  return date.toLocaleDateString();
};

// Helper to get order type display
const getOrderTypeDisplay = (type: string) => {
  switch (type) {
    case OrderType.DineIn:
      return { icon: '🍽️', label: 'Dine In', color: '#3b82f6' };
    case OrderType.Takeaway:
      return { icon: '🛍️', label: 'Takeaway', color: '#f97316' };
    case OrderType.Delivery:
      return { icon: '🚚', label: 'Delivery', color: '#8b5cf6' };
    default:
      return { icon: '📦', label: type, color: '#6b7280' };
  }
};

// Helper to get status color
const getStatusColor = (status: string) => {
  switch (status.toLowerCase()) {
    case 'pending':
      return '#fbbf24';
    case 'confirmed':
      return '#10b981';
    case 'preparing':
      return '#3b82f6';
    case 'ready':
      return '#8b5cf6';
    case 'completed':
      return '#6b7280';
    case 'cancelled':
      return '#ef4444';
    default:
      return '#6b7280';
  }
};

export default function OrderList({
  orders,
  selectedOrderId,
  onSelectOrder,
  isLoading,
  error,
}: OrderListProps) {
  const { t } = useTranslation();

  if (error) {
    return (
      <div className={styles.errorState}>
        <p className={styles.errorMessage}>{t('cashier.error_loading_orders', 'Error loading orders')}</p>
      </div>
    );
  }

  if (isLoading && orders.length === 0) {
    return (
      <div className={styles.loadingState}>
        <div className={styles.spinner}>⟳</div>
        <p>{t('cashier.loading', 'Loading orders...')}</p>
      </div>
    );
  }

  if (orders.length === 0) {
    return (
      <div className={styles.orderListEmpty}>
        {t('cashier.no_orders', 'No orders found')}
      </div>
    );
  }

  return (
    <div className={styles.orderList}>
      {orders.map((order) => {
        const orderTypeDisplay = getOrderTypeDisplay(order.type);
        const statusColor = getStatusColor(order.status);
        const isSelected = selectedOrderId === order.id;

        return (
          <div
            key={order.id}
            className={`${styles.orderCard} ${isSelected ? styles.orderCardSelected : ''}`}
            onClick={() => onSelectOrder(order.id)}
            style={{
              borderLeftColor: orderTypeDisplay.color,
            }}
          >
            <div className={styles.orderCardHeader}>
              <div className={styles.orderCardTitle}>
                <span className={styles.orderTypeIcon}>{orderTypeDisplay.icon}</span>
                <span className={styles.orderNumber}>{order.orderNumber}</span>
              </div>
              <span
                className={styles.orderStatusBadge}
                style={{ backgroundColor: statusColor }}
              >
                {t(getOrderStatusTranslationKey(order.status as OrderStatus), order.status)}
              </span>
            </div>

            <div className={styles.orderCardBody}>
              <div className={styles.orderCustomer}>
                <span className={styles.customerName}>
                  {order.customerName || t('guest', 'Guest')}
                </span>
                {order.type === OrderType.DineIn && order.tableNumber && (
                  <span className={styles.tableNumber}>
                    Table {order.tableNumber}
                  </span>
                )}
              </div>

              <div className={styles.orderCardFooter}>
                <span className={styles.orderTotal}>
                  CHF {order.total?.toFixed(2) || '0.00'}
                </span>
                <span className={styles.orderTime}>
                  {getTimeAgo(order.orderDate)}
                </span>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
