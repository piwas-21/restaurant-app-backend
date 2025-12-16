'use client';

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Clock,
  MapPin,
  User,
  Phone,
  ShoppingBag,
  CreditCard,
  Printer,
  CheckCircle,
  XCircle,
  AlertCircle,
  Zap,
  RefreshCw,
} from 'lucide-react';
import { OrderDto } from '@/types/order';
import styles from './OrderDetails.module.css';
import { getPaymentMethodLabel } from '@/utils/paymentMethodDisplay';
import { exportKitchenItemsToPDF } from '@/utils/pdfExportUtils';

interface OrderDetailsProps {
  order: OrderDto | null;
  onStatusChange: (status: string) => Promise<void>;
  onAddPayment: () => void;
  onRefund: () => void;
  onCancel: () => void;
  onToggleFocus: () => void;
  onQuickConfirm?: (orderId: string) => void;
  isLoading?: boolean;
}

export default function OrderDetails({
  order,
  onStatusChange,
  onAddPayment,
  onRefund,
  onCancel,
  onToggleFocus,
  onQuickConfirm,
  isLoading = false,
}: OrderDetailsProps) {
  const { t } = useTranslation();
  const [isUpdating, setIsUpdating] = useState(false);
  const [notesExpanded, setNotesExpanded] = useState(false);
  const [noteText, setNoteText] = useState('');

  if (!order) {
    return (
      <div className={styles.noOrderSelected}>
        <AlertCircle size={48} />
        <p>{t('cashier.select_order', 'Select an order to view details')}</p>
      </div>
    );
  }

  const handleStatusChange = async (newStatus: string) => {
    setIsUpdating(true);
    try {
      await onStatusChange(newStatus);
    } finally {
      setIsUpdating(false);
    }
  };

  const orderStatuses = ['Pending', 'Confirmed', 'Preparing', 'Ready', 'Completed'];
  const nextStatuses = orderStatuses.slice(orderStatuses.indexOf(order.status) + 1);
  const hasFrontKitchenItems = order?.items?.some(item => item.kitchenType === 'FrontKitchen');
  const hasBackKitchenItems = order?.items?.some(item => item.kitchenType === 'BackKitchen');

  const orderTypeEmoji = order.type === 'DineIn' ? '🍽️' : order.type === 'Takeaway' ? '🛍️' : '🚚';
  
  const getStatusColor = (status: string) => {
    switch (status.toLowerCase()) {
      case 'pending': return '#fbbf24';
      case 'confirmed': return '#10b981';
      case 'preparing': return '#3b82f6';
      case 'ready': return '#8b5cf6';
      case 'completed': return '#6b7280';
      case 'cancelled': return '#ef4444';
      default: return '#6b7280';
    }
  };

  return (
    <div className={styles.orderDetailsWrapper}>
      {/* Sticky Action Bar */}
      <div className={styles.stickyActionBar}>
        <div className={styles.actionBarGrid}>
          {/* Quick Confirm for Takeaway/Delivery Pending Orders */}
          {onQuickConfirm && order.status === 'Pending' && (order.type === 'Takeaway' || order.type === 'Delivery') && (
            <button
              className={`${styles.actionButton} ${styles.actionButtonSuccess}`}
              onClick={() => onQuickConfirm(order.id)}
            >
              <Zap size={18} />
              {t('cashier.quick_confirm', 'Quick Confirm')}
            </button>
          )}

          {/* Status Update */}
          {nextStatuses.length > 0 && order.status !== 'Completed' && order.status !== 'Cancelled' && (
            <select
              className={`${styles.actionButton} ${styles.actionButtonPrimary}`}
              value=""
              onChange={(e) => handleStatusChange(e.target.value)}
              disabled={isUpdating}
            >
              <option value="">{t('cashier.update_status', 'Update Status')}</option>
              {nextStatuses.map((status) => (
                <option key={status} value={status}>
                  {t(`order_status_${status.toLowerCase()}`, status)}
                </option>
              ))}
            </select>
          )}

          {/* Add Payment - hide for completed/cancelled */}
          {order.status !== 'Completed' && order.status !== 'Cancelled' && (
            <button className={`${styles.actionButton} ${styles.actionButtonSuccess}`} onClick={onAddPayment}>
              <CreditCard size={18} />
              {t('cashier.add_payment', 'Payment')}
            </button>
          )}

          {order.totalPaid > 0 && (
            <button className={`${styles.actionButton} ${styles.actionButtonWarning}`} onClick={onRefund}>
              <RefreshCw size={18} />
              {t('cashier.refund', 'Refund')}
            </button>
          )}

          {order.status !== 'Completed' && order.status !== 'Cancelled' && (
            <button className={`${styles.actionButton} ${styles.actionButtonDanger}`} onClick={onCancel}>
              <XCircle size={18} />
              {t('cancel', 'Cancel')}
            </button>
          )}

          <button 
            className={`${styles.actionButton} ${order.isFocusOrder ? styles.actionButtonSecondary : styles.actionButtonInfo}`}
            onClick={onToggleFocus}
          >
            {order.isFocusOrder ? '⭐' : '☆'} {order.isFocusOrder ? t('remove_focus', 'Unfocus') : t('cashier.mark_as_focus', 'Focus')}
          </button>
        </div>
      </div>

      {/* Header */}
      <div className={styles.orderDetailsHeader}>
        <div className={styles.orderHeaderTop}>
          <div>
            <h2 className={styles.orderTitle}>
              {orderTypeEmoji} {order.orderNumber}
            </h2>
            <p className={styles.orderSubtitle}>
              <Clock size={14} />
              {new Date(order.orderDate).toLocaleString()}
            </p>
          </div>
          <span 
            className={styles.statusBadge}
            style={{ backgroundColor: getStatusColor(order.status), color: 'white' }}
          >
            {t(`order_status_${order.status.toLowerCase()}`, order.status)}
          </span>
        </div>
      </div>

      {/* Two Column Layout */}
      <div className={styles.contentGrid}>
        {/* LEFT COLUMN - Customer & Items */}
        <div className={styles.leftColumn}>
          {/* Customer Information */}
          <div className={styles.infoCard}>
            <h3 className={styles.infoCardHeader}>
              <User size={18} />
              {t('customer', 'Customer')}
            </h3>
            <div className={styles.infoGrid}>
              {order.customerName && (
                <div className={styles.infoItem}>
                  <span className={styles.infoLabel}>{t('name', 'Name')}</span>
                  <span className={styles.infoValue}>{order.customerName}</span>
                </div>
              )}
              {order.customerEmail && (
                <div className={styles.infoItem}>
                  <span className={styles.infoLabel}>{t('email', 'Email')}</span>
                  <span className={styles.infoValue}>{order.customerEmail}</span>
                </div>
              )}
              {order.customerPhone && (
                <div className={styles.infoItem}>
                  <span className={styles.infoLabel}>{t('phone', 'Phone')}</span>
                  <span className={styles.infoValue}>{order.customerPhone}</span>
                </div>
              )}
              {order.type === 'DineIn' && order.tableNumber && (
                <div className={styles.infoItem}>
                  <span className={styles.infoLabel}>{t('table', 'Table')}</span>
                  <span className={`${styles.infoValue} ${styles.infoValueLarge}`}>#{order.tableNumber}</span>
                </div>
              )}
            </div>

            {/* Delivery Address */}
            {order.type === 'Delivery' && order.deliveryAddress && (
              <div style={{ marginTop: '1rem', paddingTop: '1rem', borderTop: '1px solid var(--border-color)' }}>
                <div className={styles.infoItem}>
                  <span className={styles.infoLabel}>
                    <MapPin size={14} style={{ display: 'inline', marginRight: '0.25rem' }} />
                    {t('delivery_address', 'Delivery Address')}
                  </span>
                  <span className={styles.infoValue}>
                    {order.deliveryAddress.addressLine1}
                    {order.deliveryAddress.addressLine2 && `, ${order.deliveryAddress.addressLine2}`}
                    <br />
                    {order.deliveryAddress.postalCode} {order.deliveryAddress.city}
                  </span>
                </div>
              </div>
            )}
          </div>

          {/* Order Items */}
          <div className={styles.infoCard}>
            <h3 className={styles.infoCardHeader}>
              <ShoppingBag size={18} />
              {t('items', 'Items')}
            </h3>
            <div className={styles.itemsList}>
              {order.items?.map((item, idx) => (
                <div key={idx} className={styles.orderItem}>
                  <div className={styles.itemInfo}>
                    <div className={styles.itemName}>
                      {item.productName}
                      {item.variationName && ` - ${item.variationName}`}
                    </div>
                    {item.specialInstructions && (
                      <div className={styles.itemInstructions}>
                        💬 {item.specialInstructions}
                      </div>
                    )}
                  </div>
                  <div className={styles.itemPricing}>
                    <div className={styles.itemQuantity}>×{item.quantity}</div>
                    <div className={styles.itemPrice}>CHF {item.itemTotal?.toFixed(2)}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* RIGHT COLUMN - Summary & Payment */}
        <div className={styles.rightColumn}>
          {/* Order Summary */}
          <div className={styles.infoCard}>
            <h3 className={styles.infoCardHeader}>{t('order_summary', 'Order Summary')}</h3>
            <div className={styles.summaryRow}>
              <span className={styles.summaryLabel}>{t('subtotal', 'Subtotal')}</span>
              <span className={styles.summaryValue}>CHF {order.subTotal?.toFixed(2)}</span>
            </div>
            {order.tax > 0 && (
              <div className={styles.summaryRow}>
                <span className={styles.summaryLabel}>{t('tax', 'Tax')}</span>
                <span className={styles.summaryValue}>CHF {order.tax?.toFixed(2)}</span>
              </div>
            )}
            {order.deliveryFee > 0 && (
              <div className={styles.summaryRow}>
                <span className={styles.summaryLabel}>{t('delivery_fee', 'Delivery Fee')}</span>
                <span className={styles.summaryValue}>CHF {order.deliveryFee?.toFixed(2)}</span>
              </div>
            )}
            {order.discount > 0 && (
              <div className={styles.summaryRow}>
                <span className={styles.summaryLabel} style={{ color: 'var(--primary-color)' }}>
                  {t('order_discount', 'Discount')}
                </span>
                <span className={styles.summaryValue} style={{ color: 'var(--primary-color)' }}>
                  -CHF {order.discount?.toFixed(2)}</span>
              </div>
            )}
            <div className={`${styles.summaryRow} ${styles.summaryTotal}`}>
              <span className={styles.summaryLabel}>{t('order_total', 'Total')}</span>
              <span className={styles.summaryValue}>CHF {order.total?.toFixed(2)}</span>
            </div>
          </div>

      {/* Payment Information */}
      <div className={styles.infoCard}>
        <h3 className={styles.infoCardHeader}>
          <CreditCard size={18} />
          {t('payment_status', 'Payment Status')}
        </h3>
        <div className={styles.summaryRow}>
          <span className={styles.summaryLabel}>{t('cashier.total_paid', 'Total Paid')}</span>
          <span className={styles.summaryValue}>CHF {order.totalPaid?.toFixed(2)}</span>
        </div>
        <div className={styles.summaryRow}>
          <span className={styles.summaryLabel}>{t('cashier.remaining', 'Remaining')}</span>
          <span 
            className={styles.summaryValue}
            style={{ color: order.remainingAmount! > 0 ? '#ef4444' : '#10b981' }}
          >
            CHF {Math.abs(order.remainingAmount || 0).toFixed(2)}
          </span>
        </div>

        {/* Payment List */}
        {order.payments && order.payments.length > 0 && (
          <div style={{ marginTop: '1rem' }}>
            {order.payments.map((payment, idx) => (
              <div key={idx} className={styles.paymentCard}>
                <div className={styles.paymentCardHeader}>
                  <div>
                    <div className={styles.paymentMethod}>{getPaymentMethodLabel(payment.paymentMethod)}</div>
                    <div className={styles.paymentDate}>
                      {payment.paymentDate ? new Date(payment.paymentDate).toLocaleString() : 'N/A'}
                    </div>
                  </div>
                  <div className={styles.paymentAmount}>CHF {payment.amount?.toFixed(2)}</div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Print Actions */}
      <div className={styles.infoCard}>
        <h3 className={styles.infoCardHeader}>
          <Printer size={18} />
          {t('cashier.print_actions', 'Print Actions')}
        </h3>
        <div className={styles.actionBarGrid}>
          <button
            className={`${styles.actionButton} ${styles.actionButtonInfo}`}
            onClick={() => exportKitchenItemsToPDF(order, 'All', t)}
          >
            <Printer size={16} />
            {t('cashier.print_bill', 'Print Bill')}
          </button>
          {hasFrontKitchenItems && (
            <button
              className={`${styles.actionButton} ${styles.actionButtonInfo}`}
              onClick={() => exportKitchenItemsToPDF(order, 'FrontKitchen', t)}
            >
              <Printer size={16} />
              {t('print_front_kitchen', 'Front Kitchen')}
            </button>
          )}
          {hasBackKitchenItems && (
            <button
              className={`${styles.actionButton} ${styles.actionButtonDanger}`}
              onClick={() => exportKitchenItemsToPDF(order, 'BackKitchen', t)}
            >
              <Printer size={16} />
              {t('print_back_kitchen', 'Back Kitchen')}
            </button>
          )}
        </div>
      </div>

        </div>
      </div>

      {/* Order Notes */}
      <div className={styles.notesSection}>
        <div className={styles.notesHeader} onClick={() => setNotesExpanded(!notesExpanded)}>
          <div className={styles.notesHeaderLeft}>
            <AlertCircle size={18} />
            <span className={styles.notesTitle}>{t('cashier.order_notes', 'Order Notes')}</span>
            {order.notes && <span className={styles.notesBadge}>1</span>}
          </div>
          <span>{notesExpanded ? '▼' : '▶'}</span>
        </div>

        {notesExpanded && (
          <div className={styles.notesContent}>
            {order.notes && (
              <div className={styles.existingNote}>
                <p className={styles.noteText}>{order.notes}</p>
              </div>
            )}

            <div className={styles.noteForm}>
              <div className={styles.noteFormHeader}>{t('cashier.add_note', 'Add Note')}</div>
              <p className={styles.noteHint}>
                {t('cashier.note_hint', 'Document order modifications, special requests, or important information.')}
              </p>
              <textarea
                className={styles.noteTextarea}
                placeholder={t('cashier.note_placeholder', 'e.g., Customer requested extra napkins...')}
                value={noteText}
                onChange={(e) => setNoteText(e.target.value)}
                rows={3}
                maxLength={500}
              />
              <div className={styles.noteFooter}>
                <span className={styles.charCount}>{noteText.length}/500</span>
                <button
                  className={styles.noteButton}
                  disabled={!noteText.trim()}
                  onClick={() => {
                    // In future, call API to save note
                    alert('Note saved (API integration pending)');
                    setNoteText('');
                  }}
                >
                  <CheckCircle size={16} />
                  {t('cashier.save_note', 'Save Note')}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
