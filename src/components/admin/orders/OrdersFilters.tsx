import React from 'react';
import { OrderStatus } from '@/types/order';
import { useTranslation } from 'react-i18next';
import { Filter, Search, Star } from 'lucide-react';
import { useOrderHelpers } from '@/hooks/useOrderHelpers';
import styles from './OrdersFilters.module.css';

interface OrdersFiltersProps {
  searchQuery: string;
  onSearchChange: (value: string) => void;
  selectedStatus: OrderStatus | 'All';
  onStatusChange: (value: OrderStatus | 'All') => void;
  selectedPaymentStatus: string;
  onPaymentStatusChange: (value: string) => void;
  selectedOrderType: string;
  onOrderTypeChange: (value: string) => void;
  showFocusOnly: boolean;
  onShowFocusOnlyChange: (value: boolean) => void;
  totalOrders: number;
  displayedOrders: number;
  searchInputRef?: React.RefObject<HTMLInputElement | null>;
}

export const OrdersFilters: React.FC<OrdersFiltersProps> = ({
  searchQuery,
  onSearchChange,
  selectedStatus,
  onStatusChange,
  selectedPaymentStatus,
  onPaymentStatusChange,
  selectedOrderType,
  onOrderTypeChange,
  showFocusOnly,
  onShowFocusOnlyChange,
  totalOrders,
  displayedOrders,
  searchInputRef,
}) => {
  const { t } = useTranslation();
  const { getStatusLabel, statusOptions } = useOrderHelpers();

  return (
    <div className={styles.filtersSection}>
      {/* Search */}
      <div className={styles.searchBox}>
        <Search size={18} />
        <input
          ref={searchInputRef}
          type="text"
          placeholder={t('search_orders', 'Search by order number, customer name, email, or phone...')}
          value={searchQuery}
          onChange={(e) => onSearchChange(e.target.value)}
          className={styles.searchInput}
        />
      </div>

      {/* Filter Row */}
      <div className={styles.filterRow}>
        <div className={styles.filterGroup}>
          <Filter size={16} />
          <select
            value={selectedStatus}
            onChange={(e) => onStatusChange(e.target.value as OrderStatus | 'All')}
            className={styles.filterSelect}
          >
            <option value="All">{t('all_statuses', 'All Statuses')}</option>
            {statusOptions.map((status) => (
              <option key={status} value={status}>
                {getStatusLabel(status)}
              </option>
            ))}
          </select>
        </div>

        <div className={styles.filterGroup}>
          <select
            value={selectedPaymentStatus}
            onChange={(e) => onPaymentStatusChange(e.target.value)}
            className={styles.filterSelect}
          >
            <option value="All">{t('all_payment_statuses', 'All Payment Statuses')}</option>
            <option value="Pending">{t('payment_status_pending', 'Pending')}</option>
            <option value="Paid">{t('payment_status_paid', 'Paid')}</option>
            <option value="PartiallyPaid">{t('payment_status_partially_paid', 'Partially Paid')}</option>
            <option value="Refunded">{t('payment_status_refunded', 'Refunded')}</option>
            <option value="Failed">{t('payment_status_failed', 'Failed')}</option>
            <option value="Overpaid">{t('payment_status_overpaid', 'Overpaid')}</option>
          </select>
        </div>

        <div className={styles.filterGroup}>
          <select
            value={selectedOrderType}
            onChange={(e) => onOrderTypeChange(e.target.value)}
            className={styles.filterSelect}
          >
            <option value="All">{t('all_order_types', 'All Order Types')}</option>
            <option value="DineIn">{t('order_type_dine_in', 'Dine In')}</option>
            <option value="Takeaway">{t('order_type_takeaway', 'Takeaway')}</option>
            <option value="Delivery">{t('order_type_delivery', 'Delivery')}</option>
          </select>
        </div>

        <label className={styles.checkboxLabel}>
          <input
            type="checkbox"
            checked={showFocusOnly}
            onChange={(e) => onShowFocusOnlyChange(e.target.checked)}
            className={styles.checkbox}
          />
          <Star size={16} />
          {t('focus_orders_only', 'Focus Orders Only')}
        </label>
      </div>

      <div className={styles.resultsInfo}>
        {t('showing_orders', `Showing ${displayedOrders} of ${totalOrders} orders`)}
      </div>
    </div>
  );
};
