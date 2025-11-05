'use client';

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { OrderDto } from '@/types/order';
import { cancelOrder, refundPayment } from '@/services/orderService';
import { exportOrderToCSV } from '@/utils/exportUtils';
import { exportOrderToPDF } from '@/utils/pdfExportUtils';
import { getStatusBadgeClasses, getPaymentBadgeClasses, getFocusBadgeClass } from '@/utils/orderStatusStyles';
import {
  X,
  User,
  MapPin,
  Phone,
  Mail,
  CreditCard,
  Package,
  Clock,
  FileText,
  Star,
  UtensilsCrossed,
  Store,
  Truck,
  Printer,
  Download,
  Ban,
  DollarSign,
  Loader2,
  ChevronDown,
} from 'lucide-react';
import Image from 'next/image';
import styles from './OrderDetailsModal.module.css';

interface OrderDetailsModalProps {
  order: OrderDto;
  onClose: () => void;
  onOrderUpdated?: (updatedOrder: OrderDto) => void;
}

export default function OrderDetailsModal({ order, onClose, onOrderUpdated }: OrderDetailsModalProps) {
  const { t } = useTranslation();
  const [showCancelModal, setShowCancelModal] = useState(false);
  const [showRefundModal, setShowRefundModal] = useState(false);
  const [cancelReason, setCancelReason] = useState('');
  const [isCancelling, setIsCancelling] = useState(false);
  const [selectedPayment, setSelectedPayment] = useState<string | null>(null);
  const [refundAmount, setRefundAmount] = useState('');
  const [refundReason, setRefundReason] = useState('');
  const [isRefunding, setIsRefunding] = useState(false);
  const [error, setError] = useState('');
  const [showExportMenu, setShowExportMenu] = useState(false);

  const canCancelOrder = () => {
    return order.status !== 'Completed' && order.status !== 'Delivered' && order.status !== 'Cancelled';
  };

  const handleCancelOrder = async () => {
    if (!cancelReason.trim()) {
      setError(t('provide_cancellation_reason', 'Please provide a cancellation reason'));
      return;
    }

    try {
      setIsCancelling(true);
      setError('');
      const updatedOrder = await cancelOrder(order.id, { reason: cancelReason });
      if (onOrderUpdated) {
        onOrderUpdated(updatedOrder);
      }
      setShowCancelModal(false);
      alert(t('order_cancelled_successfully', 'Order cancelled successfully'));
      onClose();
    } catch (err) {
      setError(t('failed_to_cancel_order', 'Failed to cancel order. Please try again.'));
    } finally {
      setIsCancelling(false);
    }
  };

  const handleRefundPayment = async () => {
    if (!selectedPayment || !refundAmount || !refundReason.trim()) {
      setError(t('fill_refund_details', 'Please fill in all refund details'));
      return;
    }

    const amount = parseFloat(refundAmount);
    if (isNaN(amount) || amount <= 0) {
      setError(t('enter_valid_refund_amount', 'Please enter a valid refund amount'));
      return;
    }

    try {
      setIsRefunding(true);
      setError('');
      await refundPayment(order.id, selectedPayment, {
        amount,
        reason: refundReason,
      });
      setShowRefundModal(false);
      alert(t('payment_refunded_successfully', 'Payment refunded successfully'));
      onClose();
    } catch (err) {
      setError(t('failed_to_process_refund', 'Failed to process refund. Please try again.'));
    } finally {
      setIsRefunding(false);
    }
  };

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

  const getOrderTypeIcon = () => {
    switch (order.type) {
      case 'DineIn':
        return <UtensilsCrossed size={20} />;
      case 'Takeaway':
        return <Store size={20} />;
      case 'Delivery':
        return <Truck size={20} />;
      default:
        return <Package size={20} />;
    }
  };

  const getOrderTypeLabel = () => {
    switch (order.type) {
      case 'DineIn':
        return t('dine_in', 'Dine In');
      case 'Takeaway':
        return t('takeaway', 'Takeaway');
      case 'Delivery':
        return t('delivery', 'Delivery');
      default:
        return order.type;
    }
  };

  const handlePrint = () => {
    // Add print-specific class to body
    document.body.classList.add('printing');

    // Small delay to ensure styles are applied before print dialog opens
    setTimeout(() => {
      window.print();

      // Remove class after print dialog closes
      setTimeout(() => {
        document.body.classList.remove('printing');
      }, 100);
    }, 10);
  };

  const handleExport = () => {
    exportOrderToCSV(order);
    setShowExportMenu(false);
  };

  const handleExportPDF = () => {
    exportOrderToPDF(order);
    setShowExportMenu(false);
  };

  return (
    <div className={styles.overlay} onClick={onClose} id="order-details-print">
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        {/* Header */}
        <div className={styles.header}>
          <div className={styles.headerLeft}>
            <h2 className={styles.title}>{t('order_details', 'Order Details')}</h2>
            <div className={styles.orderMeta}>
              <span className={styles.orderNumber}>#{order.orderNumber}</span>
              <span className={getStatusBadgeClasses(order.status)}>
                {order.status}
              </span>
              {order.isFocusOrder && (
                <span className={getFocusBadgeClass()}>
                  <Star size={14} />
                  Focus Order
                </span>
              )}
            </div>
          </div>
          <div className={styles.headerActions}>
            <button
              onClick={handlePrint}
              className={styles.iconButton}
              title={t('print_receipt', 'Print Receipt')}
            >
              <Printer size={20} />
            </button>
            <div className={styles.exportDropdown}>
              <button
                onClick={() => setShowExportMenu(!showExportMenu)}
                className={styles.iconButton}
                title={t('export', 'Export')}
              >
                <Download size={20} />
                <ChevronDown size={14} />
              </button>
              {showExportMenu && (
                <div className={styles.exportMenu}>
                  <button onClick={handleExport} className={styles.exportMenuItem}>
                    <FileText size={16} />
                    {t('export_as_csv', 'Export as CSV')}
                  </button>
                  <button onClick={handleExportPDF} className={styles.exportMenuItem}>
                    <FileText size={16} />
                    {t('export_as_pdf', 'Export as PDF')}
                  </button>
                </div>
              )}
            </div>
            <button onClick={onClose} className={styles.closeButton}>
              <X size={24} />
            </button>
          </div>
        </div>

        {/* Content */}
        <div className={styles.content}>
          {/* Order Info Section */}
          <div className={styles.section}>
            <h3 className={styles.sectionTitle}>
              <Package size={18} />
              {t('order_information', 'Order Information')}
            </h3>
            <div className={styles.infoGrid}>
              <div className={styles.infoItem}>
                <span className={styles.infoLabel}>{t('order_type', 'Order Type')}</span>
                <div className={styles.infoValue}>
                  {getOrderTypeIcon()}
                  {getOrderTypeLabel()}
                </div>
              </div>
              <div className={styles.infoItem}>
                <span className={styles.infoLabel}>{t('order_date', 'Order Date')}</span>
                <div className={styles.infoValue}>
                  <Clock size={16} />
                  {formatDate(order.orderDate)}
                </div>
              </div>
              {order.type === 'DineIn' && order.tableNumber && (
                <div className={styles.infoItem}>
                  <span className={styles.infoLabel}>{t('table_number', 'Table Number')}</span>
                  <div className={styles.infoValue}>
                    <UtensilsCrossed size={16} />
                    {t('table', 'Table')} {order.tableNumber}
                  </div>
                </div>
              )}
              <div className={styles.infoItem}>
                <span className={styles.infoLabel}>{t('payment_status', 'Payment Status')}</span>
                <div className={styles.infoValue}>
                  <CreditCard size={16} />
                  {order.isFullyPaid ? t('paid', 'Paid') : t('pending', 'Pending')}
                </div>
              </div>
            </div>
          </div>

          {/* Customer Info Section */}
          <div className={styles.section}>
            <h3 className={styles.sectionTitle}>
              <User size={18} />
              {t('customer_information', 'Customer Information')}
            </h3>
            <div className={styles.customerInfo}>
              {order.customerName && (
                <div className={styles.customerDetail}>
                  <User size={16} />
                  <span>{order.customerName}</span>
                </div>
              )}
              {order.customerEmail && (
                <div className={styles.customerDetail}>
                  <Mail size={16} />
                  <a href={`mailto:${order.customerEmail}`}>{order.customerEmail}</a>
                </div>
              )}
              {order.customerPhone && (
                <div className={styles.customerDetail}>
                  <Phone size={16} />
                  <a href={`tel:${order.customerPhone}`}>{order.customerPhone}</a>
                </div>
              )}
            </div>
          </div>

          {/* Delivery Address Section */}
          {order.type === 'Delivery' && order.deliveryAddress && (
            <div className={styles.section}>
              <h3 className={styles.sectionTitle}>
                <MapPin size={18} />
                {t('delivery_address', 'Delivery Address')}
              </h3>
              <div className={styles.addressCard}>
                <p className={styles.addressLine}>{order.deliveryAddress.addressLine1}</p>
                {order.deliveryAddress.addressLine2 && (
                  <p className={styles.addressLine}>{order.deliveryAddress.addressLine2}</p>
                )}
                <p className={styles.addressLine}>
                  {order.deliveryAddress.postalCode} {order.deliveryAddress.city}
                </p>
                {order.deliveryAddress.deliveryInstructions && (
                  <p className={styles.deliveryInstructions}>
                    <FileText size={14} />
                    {order.deliveryAddress.deliveryInstructions}
                  </p>
                )}
              </div>
            </div>
          )}

          {/* Order Items Section */}
          <div className={styles.section}>
            <h3 className={styles.sectionTitle}>
              <Package size={18} />
              {t('order_items', 'Order Items')} ({order.items.length})
            </h3>
            <div className={styles.itemsList}>
              {order.items.map((item) => (
                <div key={item.id} className={styles.orderItem}>
                  {item.productImageUrl && (
                    <div className={styles.itemImage}>
                      <Image
                        src={item.productImageUrl}
                        alt={item.productName || ''}
                        width={60}
                        height={60}
                        style={{ objectFit: 'cover' }}
                      />
                    </div>
                  )}
                  <div className={styles.itemDetails}>
                    <h4 className={styles.itemName}>{item.productName}</h4>
                    {item.variationName && (
                      <p className={styles.itemVariation}>{item.variationName}</p>
                    )}
                    {item.specialInstructions && (
                      <p className={styles.itemInstructions}>
                        <FileText size={12} />
                        {item.specialInstructions}
                      </p>
                    )}
                    <div className={styles.itemQuantity}>
                      {t('qty', 'Qty')}: {item.quantity} × {formatPrice(item.unitPrice)}
                    </div>
                  </div>
                  <div className={styles.itemTotal}>
                    {formatPrice(item.itemTotal)}
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Order Summary Section */}
          <div className={styles.section}>
            <h3 className={styles.sectionTitle}>
              <CreditCard size={18} />
              {t('order_summary', 'Order Summary')}
            </h3>
            <div className={styles.summary}>
              <div className={styles.summaryRow}>
                <span>{t('subtotal', 'Subtotal')}</span>
                <span>{formatPrice(order.subTotal)}</span>
              </div>

              {order.discount > 0 && (
                <div className={`${styles.summaryRow} ${styles.discount}`}>
                  <span>{t('promo_discount', 'Promo Discount')}</span>
                  <span>-{formatPrice(order.discount)}</span>
                </div>
              )}

              {order.hasUserLimitDiscount && order.userLimitAmount > 0 && (
                <div className={`${styles.summaryRow} ${styles.discount}`}>
                  <span>{t('user_limit_discount', 'User Limit Discount')}</span>
                  <span>-{formatPrice(order.userLimitAmount)}</span>
                </div>
              )}

              {order.deliveryFee > 0 && (
                <div className={styles.summaryRow}>
                  <span>{t('delivery_fee', 'Delivery Fee')}</span>
                  <span>{formatPrice(order.deliveryFee)}</span>
                </div>
              )}

              <div className={styles.summaryRow}>
                <span>{t('tax', 'Tax')}</span>
                <span>{formatPrice(order.tax)}</span>
              </div>

              <div className={`${styles.summaryRow} ${styles.total}`}>
                <span>Total</span>
                <span>{formatPrice(order.total)}</span>
              </div>
            </div>
          </div>

          {/* Payment Details Section */}
          {order.payments && order.payments.length > 0 && (
            <div className={styles.section}>
              <h3 className={styles.sectionTitle}>
                <CreditCard size={18} />
                {t('payment_details', 'Payment Details')}
              </h3>
              <div className={styles.paymentsList}>
                {order.payments.map((payment) => (
                  <div key={payment.id} className={styles.paymentItem}>
                    <div className={styles.paymentMethod}>
                      <CreditCard size={16} />
                      <span>{payment.paymentMethod}</span>
                    </div>
                    <div className={styles.paymentAmount}>
                      {formatPrice(payment.amount)}
                    </div>
                    <div className={styles.paymentStatus}>
                      <span className={getPaymentBadgeClasses(payment.status)}>
                        {payment.status}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Notes Section */}
          {order.notes && (
            <div className={styles.section}>
              <h3 className={styles.sectionTitle}>
                <FileText size={18} />
                {t('notes', 'Notes')}
              </h3>
              <div className={styles.notesCard}>
                <p>{order.notes}</p>
              </div>
            </div>
          )}

          {/* Status History Section */}
          {order.statusHistory && order.statusHistory.length > 0 && (
            <div className={styles.section}>
              <h3 className={styles.sectionTitle}>
                <Clock size={18} />
                {t('action_history', 'Action History')}
              </h3>
              <div className={styles.timeline}>
                {order.statusHistory.map((history) => (
                  <div key={history.id} className={styles.timelineItem}>
                    <div className={styles.timelineDot}></div>
                    <div className={styles.timelineContent}>
                      <div className={styles.timelineStatus}>{history.status}</div>
                      <div className={styles.timelineDate}>
                        {formatDate(history.changedAt)}
                      </div>
                      {history.notes && (
                        <div className={styles.timelineNotes}>{history.notes}</div>
                      )}
                      {history.changedBy && (
                        <div className={styles.timelineBy}>by {history.changedBy}</div>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className={styles.footer}>
          <div className={styles.footerActions}>
            {canCancelOrder() && (
              <button
                onClick={() => setShowCancelModal(true)}
                className={styles.cancelOrderButton}
              >
                <Ban size={18} />
                {t('cancel_order', 'Cancel Order')}
              </button>
            )}
            {order.payments && order.payments.length > 0 && order.isFullyPaid && (
              <button
                onClick={() => setShowRefundModal(true)}
                className={styles.refundButton}
              >
                <DollarSign size={18} />
                {t('refund_payment', 'Refund Payment')}
              </button>
            )}
          </div>
          <button onClick={onClose} className={styles.closeButtonFooter}>
            {t('close', 'Close')}
          </button>
        </div>

        {/* Cancel Order Modal */}
        {showCancelModal && (
          <div className={styles.confirmModal}>
            <div className={styles.confirmModalContent}>
              <h3 className={styles.confirmModalTitle}>
                <Ban size={20} />
                {t('cancel_order', 'Cancel Order')}
              </h3>
              <p className={styles.confirmModalMessage}>
                {t('cancel_order_warning', 'Are you sure you want to cancel this order? This action cannot be undone.')}
              </p>
              <div className={styles.formGroup}>
                <label htmlFor="cancelReason">{t('cancellation_reason', 'Cancellation Reason')} *</label>
                <textarea
                  id="cancelReason"
                  value={cancelReason}
                  onChange={(e) => setCancelReason(e.target.value)}
                  placeholder={t('cancellation_reason_placeholder', 'Enter reason for cancellation...')}
                  className={styles.textarea}
                  rows={4}
                />
              </div>
              {error && <div className={styles.errorMessage}>{error}</div>}
              <div className={styles.confirmModalActions}>
                <button
                  onClick={() => {
                    setShowCancelModal(false);
                    setCancelReason('');
                    setError('');
                  }}
                  className={styles.cancelButton}
                  disabled={isCancelling}
                >
                  {t('cancel', 'Cancel')}
                </button>
                <button
                  onClick={handleCancelOrder}
                  className={styles.confirmCancelButton}
                  disabled={isCancelling}
                >
                  {isCancelling ? (
                    <>
                      <Loader2 size={18} className={styles.spinner} />
                      {t('cancelling', 'Cancelling...')}
                    </>
                  ) : (
                    <>
                      <Ban size={18} />
                      {t('cancel_order', 'Cancel Order')}
                    </>
                  )}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Refund Payment Modal */}
        {showRefundModal && (
          <div className={styles.confirmModal}>
            <div className={styles.confirmModalContent}>
              <h3 className={styles.confirmModalTitle}>
                <DollarSign size={20} />
                {t('refund_payment', 'Refund Payment')}
              </h3>
              <p className={styles.confirmModalMessage}>
                {t('refund_payment_warning', 'This will process a refund for the selected payment.')}
              </p>
              <div className={styles.formGroup}>
                <label htmlFor="paymentSelect">{t('select_payment', 'Select Payment')} *</label>
                <select
                  id="paymentSelect"
                  value={selectedPayment || ''}
                  onChange={(e) => setSelectedPayment(e.target.value)}
                  className={styles.select}
                >
                  <option value="">{t('select_payment_to_refund', '-- Select Payment --')}</option>
                  {order.payments.map((payment) => (
                    <option key={payment.id} value={payment.id}>
                      {payment.paymentMethod} - {formatPrice(payment.amount)}
                    </option>
                  ))}
                </select>
              </div>
              <div className={styles.formGroup}>
                <label htmlFor="refundAmount">{t('refund_amount', 'Refund Amount')} (CHF) *</label>
                <input
                  id="refundAmount"
                  type="number"
                  step="0.01"
                  min="0"
                  value={refundAmount}
                  onChange={(e) => setRefundAmount(e.target.value)}
                  placeholder="0.00"
                  className={styles.input}
                />
              </div>
              <div className={styles.formGroup}>
                <label htmlFor="refundReason">{t('refund_reason', 'Refund Reason')} *</label>
                <textarea
                  id="refundReason"
                  value={refundReason}
                  onChange={(e) => setRefundReason(e.target.value)}
                  placeholder={t('refund_reason_placeholder', 'Enter reason for refund...')}
                  className={styles.textarea}
                  rows={4}
                />
              </div>
              {error && <div className={styles.errorMessage}>{error}</div>}
              <div className={styles.confirmModalActions}>
                <button
                  onClick={() => {
                    setShowRefundModal(false);
                    setSelectedPayment(null);
                    setRefundAmount('');
                    setRefundReason('');
                    setError('');
                  }}
                  className={styles.cancelButton}
                  disabled={isRefunding}
                >
                  {t('cancel', 'Cancel')}
                </button>
                <button
                  onClick={handleRefundPayment}
                  className={styles.confirmRefundButton}
                  disabled={isRefunding}
                >
                  {isRefunding ? (
                    <>
                      <Loader2 size={18} className={styles.spinner} />
                      {t('refunding', 'Refunding...')}
                    </>
                  ) : (
                    <>
                      <DollarSign size={18} />
                      {t('refund_payment', 'Refund Payment')}
                    </>
                  )}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
