import React from 'react';
import { useTranslation } from 'react-i18next';
import { OrderStatus } from '@/types/order';
import { UtensilsCrossed, Store, Truck, Package } from 'lucide-react';

/**
 * Custom hook providing utility functions for order management
 */
export const useOrderHelpers = () => {
  const { t } = useTranslation();

  const formatPrice = (price: number) => {
    return new Intl.NumberFormat('de-CH', {
      style: 'currency',
      currency: 'CHF',
    }).format(price);
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleString('de-CH', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  const getOrderTypeIcon = (orderType: string): React.ReactNode => {
    switch (orderType) {
      case 'DineIn':
        return <UtensilsCrossed size={16} />;
      case 'Takeaway':
        return <Store size={16} />;
      case 'Delivery':
        return <Truck size={16} />;
      default:
        return <Package size={16} />;
    }
  };

  const getOrderTypeLabel = (orderType: string) => {
    switch (orderType) {
      case 'DineIn':
        return t('order_type_dine_in', 'Dine In');
      case 'Takeaway':
        return t('order_type_takeaway', 'Takeaway');
      case 'Delivery':
        return t('order_type_delivery', 'Delivery');
      default:
        return orderType;
    }
  };

  const getStatusLabel = (status: string) => {
    switch (status) {
      case 'Pending':
        return t('order_status_pending', 'Pending');
      case 'Confirmed':
        return t('order_status_confirmed', 'Confirmed');
      case 'Preparing':
        return t('order_status_preparing', 'Preparing');
      case 'Ready':
        return t('order_status_ready', 'Ready');
      case 'InTransit':
        return t('order_status_in_transit', 'In Transit');
      case 'Delivered':
        return t('order_status_delivered', 'Delivered');
      case 'Completed':
        return t('order_status_completed', 'Completed');
      case 'Cancelled':
        return t('order_status_cancelled', 'Cancelled');
      default:
        return status;
    }
  };

  const statusOptions: OrderStatus[] = [
    'Pending',
    'Confirmed',
    'Preparing',
    'Ready',
    'InTransit',
    'Delivered',
    'Completed',
    'Cancelled',
  ];

  return {
    formatPrice,
    formatDate,
    getOrderTypeIcon,
    getOrderTypeLabel,
    getStatusLabel,
    statusOptions,
  };
};
