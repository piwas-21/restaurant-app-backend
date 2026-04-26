import React from 'react';
import { useTranslation } from 'react-i18next';
import { OrderDto } from '@/types/order';
import OrderList from './OrderList';
import OrderDetails from './OrderDetails';
import styles from './CashierMainContent.module.css';

interface CashierMainContentProps {
  filteredOrders: OrderDto[];
  selectedOrder: OrderDto | null;
  selectedOrderId: string | null;
  isLoading: boolean;
  error: string | null;
  searchQuery: string;
  statusFilter: string;
  paymentStatusFilter: string;
  orderTypeFilter: string;
  onSelectOrder: (orderId: string) => void;
  onStatusChange: (newStatus: string) => Promise<void>;
  onAddPayment: () => void;
  onRefund: () => void;
  onCancel: () => void;
  onToggleFocus: () => void;
  onQuickConfirm: (orderId: string) => void;
  onSearchChange: (query: string) => void;
  onStatusFilterChange: (status: string) => void;
  onPaymentStatusFilterChange: (status: string) => void;
  onOrderTypeFilterChange: (type: string) => void;
}

export default function CashierMainContent({
  filteredOrders,
  selectedOrder,
  selectedOrderId,
  isLoading,
  error,
  searchQuery,
  statusFilter,
  paymentStatusFilter,
  orderTypeFilter,
  onSelectOrder,
  onStatusChange,
  onAddPayment,
  onRefund,
  onCancel,
  onToggleFocus,
  onQuickConfirm,
  onSearchChange,
  onStatusFilterChange,
  onPaymentStatusFilterChange,
  onOrderTypeFilterChange
}: CashierMainContentProps) {
  const { t } = useTranslation();

  return (
    <div className={styles.container}>
      <div className={styles.content}>
        {/* Sidebar - Order List */}
        <div className={styles.sidebar}>
          <div className={styles.sidebarHeader}>
            <h2 className={styles.sidebarTitle}>{t('cashier.orders') || 'Orders'}</h2>

            {/* Filters inside sidebar */}
            <div className={styles.filterSection}>
              <input
                type="text"
                className={styles.searchInput}
                placeholder={t('cashier.search_placeholder') || 'Search by order # or customer...'}
                value={searchQuery}
                onChange={(e) => onSearchChange(e.target.value)}
              />

              <div className={styles.filterSelects}>
                <select
                  className={styles.filterSelect}
                  value={statusFilter}
                  onChange={(e) => onStatusFilterChange(e.target.value)}
                  title={t('cashier.filter_status') || 'Filter by status'}
                >
                  <option value="all">All Statuses</option>
                  <option value="Pending">Pending</option>
                  <option value="Confirmed">Confirmed</option>
                  <option value="Preparing">Preparing</option>
                  <option value="Ready">Ready</option>
                  <option value="Completed">Completed</option>
                  <option value="Cancelled">Cancelled</option>
                </select>

                <select
                  className={styles.filterSelect}
                  value={paymentStatusFilter}
                  onChange={(e) => onPaymentStatusFilterChange(e.target.value)}
                  title={t('cashier.filter_payment_status') || 'Filter by payment status'}
                >
                  <option value="all">All Payment Statuses</option>
                  <option value="Pending">Pending</option>
                  <option value="Paid">Paid</option>
                  <option value="PartiallyPaid">Partially Paid</option>
                  <option value="Refunded">Refunded</option>
                </select>

                <select
                  className={styles.filterSelect}
                  value={orderTypeFilter}
                  onChange={(e) => onOrderTypeFilterChange(e.target.value)}
                  title={t('cashier.filter_type') || 'Filter by order type'}
                >
                  <option value="all">All Types</option>
                  <option value="DineIn">Dine In</option>
                  <option value="Takeaway">Takeaway</option>
                  <option value="Delivery">Delivery</option>
                </select>
              </div>
            </div>
          </div>

          {/* Order List */}
          {isLoading && !filteredOrders.length ? (
            <div className={styles.orderListEmpty}>
              <span>{t('cashier.loading') || 'Loading orders...'}</span>
            </div>
          ) : filteredOrders.length === 0 ? (
            <div className={styles.orderListEmpty}>
              <span>{t('cashier.no_orders') || 'No orders found'}</span>
            </div>
          ) : (
            <div className={styles.orderList}>
              <OrderList
                orders={filteredOrders}
                selectedOrderId={selectedOrderId}
                onSelectOrder={onSelectOrder}
                isLoading={isLoading}
                error={error}
              />
            </div>
          )}
        </div>

        {/* Main Area - Order Details */}
        <div className={styles.main}>
          <div className={styles.detailsContainer}>
            {selectedOrder ? (
              <OrderDetails
                order={selectedOrder}
                onStatusChange={onStatusChange}
                onAddPayment={onAddPayment}
                onRefund={onRefund}
                onCancel={onCancel}
                onToggleFocus={onToggleFocus}
                onQuickConfirm={onQuickConfirm}
              />
            ) : (
              <div className={styles.noOrderSelected}>
                <span>{t('cashier.select_order') || 'Select an order to view details'}</span>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
