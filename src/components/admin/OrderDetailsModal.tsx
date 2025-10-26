'use client';

import React, { useState } from 'react';
import { OrderDto } from '@/types/order';
import { cancelOrder, refundPayment } from '@/services/orderService';
import { exportOrderToCSV } from '@/utils/exportUtils';
import { exportOrderToPDF } from '@/utils/pdfExportUtils';
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
      setError('Please provide a cancellation reason');
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
      alert('Order cancelled successfully');
      onClose();
    } catch (err) {
      setError('Failed to cancel order. Please try again.');
    } finally {
      setIsCancelling(false);
    }
  };

  const handleRefundPayment = async () => {
    if (!selectedPayment || !refundAmount || !refundReason.trim()) {
      setError('Please fill in all refund details');
      return;
    }

    const amount = parseFloat(refundAmount);
    if (isNaN(amount) || amount <= 0) {
      setError('Please enter a valid refund amount');
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
      alert('Payment refunded successfully');
      onClose();
    } catch (err) {
      setError('Failed to process refund. Please try again.');
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
        return 'Dine In';
      case 'Takeaway':
        return 'Takeaway';
      case 'Delivery':
        return 'Delivery';
      default:
        return order.type;
    }
  };

  const getStatusColor = () => {
    switch (order.status) {
      case 'Pending':
      case 'Confirmed':
        return styles.statusPending;
      case 'Preparing':
        return styles.statusPreparing;
      case 'Ready':
      case 'InTransit':
        return styles.statusReady;
      case 'Delivered':
      case 'Completed':
        return styles.statusCompleted;
      case 'Cancelled':
        return styles.statusCancelled;
      default:
        return '';
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
            <h2 className={styles.title}>Order Details</h2>
            <div className={styles.orderMeta}>
              <span className={styles.orderNumber}>#{order.orderNumber}</span>
              <span className={`${styles.statusBadge} ${getStatusColor()}`}>
                {order.status}
              </span>
              {order.isFocusOrder && (
                <span className={styles.focusBadge}>
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
              title="Print Receipt"
            >
              <Printer size={20} />
            </button>
            <div className={styles.exportDropdown}>
              <button
                onClick={() => setShowExportMenu(!showExportMenu)}
                className={styles.iconButton}
                title="Export"
              >
                <Download size={20} />
                <ChevronDown size={14} />
              </button>
              {showExportMenu && (
                <div className={styles.exportMenu}>
                  <button onClick={handleExport} className={styles.exportMenuItem}>
                    <FileText size={16} />
                    Export as CSV
                  </button>
                  <button onClick={handleExportPDF} className={styles.exportMenuItem}>
                    <FileText size={16} />
                    Export as PDF
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
              Order Information
            </h3>
            <div className={styles.infoGrid}>
              <div className={styles.infoItem}>
                <span className={styles.infoLabel}>Order Type</span>
                <div className={styles.infoValue}>
                  {getOrderTypeIcon()}
                  {getOrderTypeLabel()}
                </div>
              </div>
              <div className={styles.infoItem}>
                <span className={styles.infoLabel}>Order Date</span>
                <div className={styles.infoValue}>
                  <Clock size={16} />
                  {formatDate(order.orderDate)}
                </div>
              </div>
              {order.type === 'DineIn' && order.tableNumber && (
                <div className={styles.infoItem}>
                  <span className={styles.infoLabel}>Table Number</span>
                  <div className={styles.infoValue}>
                    <UtensilsCrossed size={16} />
                    Table {order.tableNumber}
                  </div>
                </div>
              )}
              <div className={styles.infoItem}>
                <span className={styles.infoLabel}>Payment Status</span>
                <div className={styles.infoValue}>
                  <CreditCard size={16} />
                  {order.isFullyPaid ? 'Paid' : 'Pending'}
                </div>
              </div>
            </div>
          </div>

          {/* Customer Info Section */}
          <div className={styles.section}>
            <h3 className={styles.sectionTitle}>
              <User size={18} />
              Customer Information
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
                Delivery Address
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
              Order Items ({order.items.length})
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
                      Qty: {item.quantity} × {formatPrice(item.unitPrice)}
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
              Order Summary
            </h3>
            <div className={styles.summary}>
              <div className={styles.summaryRow}>
                <span>Subtotal</span>
                <span>{formatPrice(order.subTotal)}</span>
              </div>

              {order.discount > 0 && (
                <div className={`${styles.summaryRow} ${styles.discount}`}>
                  <span>Promo Discount</span>
                  <span>-{formatPrice(order.discount)}</span>
                </div>
              )}

              {order.hasUserLimitDiscount && order.userLimitAmount > 0 && (
                <div className={`${styles.summaryRow} ${styles.discount}`}>
                  <span>User Limit Discount</span>
                  <span>-{formatPrice(order.userLimitAmount)}</span>
                </div>
              )}

              {order.deliveryFee > 0 && (
                <div className={styles.summaryRow}>
                  <span>Delivery Fee</span>
                  <span>{formatPrice(order.deliveryFee)}</span>
                </div>
              )}

              <div className={styles.summaryRow}>
                <span>Tax</span>
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
                Payment Details
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
                      {payment.status}
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
                Order Notes
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
                Status History
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
                Cancel Order
              </button>
            )}
            {order.payments && order.payments.length > 0 && order.isFullyPaid && (
              <button
                onClick={() => setShowRefundModal(true)}
                className={styles.refundButton}
              >
                <DollarSign size={18} />
                Refund Payment
              </button>
            )}
          </div>
          <button onClick={onClose} className={styles.closeButtonFooter}>
            Close
          </button>
        </div>

        {/* Cancel Order Modal */}
        {showCancelModal && (
          <div className={styles.confirmModal}>
            <div className={styles.confirmModalContent}>
              <h3 className={styles.confirmModalTitle}>
                <Ban size={20} />
                Cancel Order
              </h3>
              <p className={styles.confirmModalMessage}>
                Are you sure you want to cancel this order? This action cannot be undone.
              </p>
              <div className={styles.formGroup}>
                <label htmlFor="cancelReason">Cancellation Reason *</label>
                <textarea
                  id="cancelReason"
                  value={cancelReason}
                  onChange={(e) => setCancelReason(e.target.value)}
                  placeholder="Enter reason for cancellation..."
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
                  Cancel
                </button>
                <button
                  onClick={handleCancelOrder}
                  className={styles.confirmCancelButton}
                  disabled={isCancelling}
                >
                  {isCancelling ? (
                    <>
                      <Loader2 size={18} className={styles.spinner} />
                      Cancelling...
                    </>
                  ) : (
                    <>
                      <Ban size={18} />
                      Confirm Cancellation
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
                Refund Payment
              </h3>
              <p className={styles.confirmModalMessage}>
                Select a payment to refund and enter the refund details.
              </p>
              <div className={styles.formGroup}>
                <label htmlFor="paymentSelect">Select Payment *</label>
                <select
                  id="paymentSelect"
                  value={selectedPayment || ''}
                  onChange={(e) => setSelectedPayment(e.target.value)}
                  className={styles.select}
                >
                  <option value="">-- Select Payment --</option>
                  {order.payments.map((payment) => (
                    <option key={payment.id} value={payment.id}>
                      {payment.paymentMethod} - {formatPrice(payment.amount)}
                    </option>
                  ))}
                </select>
              </div>
              <div className={styles.formGroup}>
                <label htmlFor="refundAmount">Refund Amount (CHF) *</label>
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
                <label htmlFor="refundReason">Refund Reason *</label>
                <textarea
                  id="refundReason"
                  value={refundReason}
                  onChange={(e) => setRefundReason(e.target.value)}
                  placeholder="Enter reason for refund..."
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
                  Cancel
                </button>
                <button
                  onClick={handleRefundPayment}
                  className={styles.confirmRefundButton}
                  disabled={isRefunding}
                >
                  {isRefunding ? (
                    <>
                      <Loader2 size={18} className={styles.spinner} />
                      Processing...
                    </>
                  ) : (
                    <>
                      <DollarSign size={18} />
                      Confirm Refund
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
