import React from 'react';
import { OrderDto } from '@/types/order';
import { useTranslation } from 'react-i18next';
import { Eye, RefreshCw, Star, CheckCircle, Clock } from 'lucide-react';
import { useOrderHelpers } from '@/hooks/useOrderHelpers';
import styles from './OrdersTable.module.css';

interface OrdersTableProps {
  orders: OrderDto[];
  selectedOrderIds: Set<string>;
  onToggleSelection: (orderId: string) => void;
  onToggleSelectAll: () => void;
  onViewDetails: (order: OrderDto) => void;
  onUpdateStatus: (order: OrderDto) => void;
  onToggleFocus: (order: OrderDto) => void;
  getStatusColor: (status: string) => string;
}

export const OrdersTable: React.FC<OrdersTableProps> = ({
  orders,
  selectedOrderIds,
  onToggleSelection,
  onToggleSelectAll,
  onViewDetails,
  onUpdateStatus,
  onToggleFocus,
  getStatusColor,
}) => {
  const { t } = useTranslation();
  const { formatPrice, formatDate, getOrderTypeIcon, getOrderTypeLabel, getStatusLabel } = useOrderHelpers();

  const allSelected = orders.length > 0 && selectedOrderIds.size === orders.length;

  return (
    <div className={styles.tableWrapper}>
      <table className={styles.ordersTable}>
        <thead>
          <tr>
            <th className={styles.checkboxColumn}>
              <input
                type="checkbox"
                checked={allSelected}
                onChange={onToggleSelectAll}
                className={styles.checkbox}
              />
            </th>
            <th>{t('order_number', 'Order #')}</th>
            <th>{t('customer', 'Customer')}</th>
            <th>{t('type', 'Type')}</th>
            <th>{t('status', 'Status')}</th>
            <th>{t('payment', 'Payment')}</th>
            <th>{t('total', 'Total')}</th>
            <th>{t('date', 'Date')}</th>
            <th>{t('actions', 'Actions')}</th>
          </tr>
        </thead>
        <tbody>
          {orders.map((order) => (
            <tr key={order.id} className={order.isFocusOrder ? styles.focusRow : ''}>
              <td className={styles.checkboxColumn}>
                <input
                  type="checkbox"
                  checked={selectedOrderIds.has(order.id)}
                  onChange={() => onToggleSelection(order.id)}
                  className={styles.checkbox}
                  onClick={(e) => e.stopPropagation()}
                />
              </td>
              <td>
                <div className={styles.orderNumberCell}>
                  {order.isFocusOrder && <Star size={14} className={styles.focusIcon} />}
                  <span className={styles.orderNumber}>{order.orderNumber}</span>
                </div>
              </td>
              <td>
                <div className={styles.customerCell}>
                  <span className={styles.customerName}>{order.customerName || 'N/A'}</span>
                  <span className={styles.customerEmail}>{order.customerEmail}</span>
                </div>
              </td>
              <td>
                <div className={styles.typeCell}>
                  {getOrderTypeIcon(order.type)}
                  <span>{getOrderTypeLabel(order.type)}</span>
                </div>
              </td>
              <td>
                <span className={`${styles.statusBadge} ${getStatusColor(order.status)}`}>
                  {getStatusLabel(order.status)}
                </span>
              </td>
              <td>
                <span className={styles.paymentStatus}>
                  {order.isFullyPaid ? (
                    <>
                      <CheckCircle size={14} /> {t('paid', 'Paid')}
                    </>
                  ) : (
                    <>
                      <Clock size={14} /> {t('pending', 'Pending')}
                    </>
                  )}
                </span>
              </td>
              <td className={styles.totalCell}>{formatPrice(order.total)}</td>
              <td className={styles.dateCell}>{formatDate(order.orderDate)}</td>
              <td>
                <div className={styles.actionsCell}>
                  <button
                    onClick={() => onViewDetails(order)}
                    className={styles.actionButton}
                    title={t('view_details', 'View Details')}
                  >
                    <Eye size={16} />
                  </button>
                  <button
                    onClick={() => onUpdateStatus(order)}
                    className={styles.actionButton}
                    title={t('update_status', 'Update Status')}
                  >
                    <RefreshCw size={16} />
                  </button>
                  <button
                    onClick={() => onToggleFocus(order)}
                    className={`${styles.actionButton} ${order.isFocusOrder ? styles.focusActive : ''}`}
                    title={
                      order.isFocusOrder
                        ? t('remove_focus', 'Remove Focus')
                        : t('mark_as_focus', 'Mark as Focus')
                    }
                  >
                    <Star size={16} />
                  </button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};
