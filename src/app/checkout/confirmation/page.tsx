"use client";

import React, { useEffect, useState, Suspense } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { getOrderById } from '@/services/orderService';
import { OrderDto } from '@/types/order';
import {
  CheckCircle,
  Clock,
  MapPin,
  User,
  Phone,
  Mail,
  ShoppingBag,
  ArrowRight,
  Receipt,
  Loader2,
  AlertCircle,
  Home,
  Package,
} from 'lucide-react';
import Image from 'next/image';
import styles from '../../styles/ConfirmationPage.module.css';

function ConfirmationContent() {
  const { t } = useTranslation();
  const router = useRouter();
  const searchParams = useSearchParams();
  
  const orderId = searchParams.get('orderId');
  const orderNumber = searchParams.get('orderNumber');

  const [order, setOrder] = useState<OrderDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!orderId) {
      setError(t('order_id_missing', 'Order ID is missing'));
      setIsLoading(false);
      return;
    }

    const fetchOrder = async () => {
      try {
        setIsLoading(true);
        const orderData = await getOrderById(orderId);
        setOrder(orderData);
      } catch (err) {
        // eslint-disable-next-line no-console
        console.error('Error fetching order:', err);
        setError(t('failed_to_load_order', 'Failed to load order details'));
      } finally {
        setIsLoading(false);
      }
    };

    fetchOrder();
  }, [orderId, t]);

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

  const getEstimatedTime = (orderType: string) => {
    switch (orderType) {
      case 'DineIn':
        return t('estimated_time_dine_in', '15-20 minutes');
      case 'Takeaway':
        return t('estimated_time_takeaway', '20-25 minutes');
      case 'Delivery':
        return t('estimated_time_delivery', '30-45 minutes');
      default:
        return t('estimated_time_default', '20-30 minutes');
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
      default:
        return status;
    }
  };

  if (isLoading) {
    return (
      <main className={styles.container}>
        <div className={styles.loadingState}>
          <Loader2 size={64} className={styles.spinner} />
          <p>{t('loading_order', 'Loading your order...')}</p>
        </div>
      </main>
    );
  }

  if (error || !order) {
    return (
      <main className={styles.container}>
        <div className={styles.errorState}>
          <AlertCircle size={64} className={styles.errorIcon} />
          <h1>{t('error', 'Error')}</h1>
          <p>{error || t('order_not_found', 'Order not found')}</p>
          <button onClick={() => router.push('/menu')} className={styles.menuButton}>
            {t('back_to_menu', 'Back to Menu')}
          </button>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.container}>
      <div className={styles.content}>
        {/* Success Header */}
        <div className={styles.successHeader}>
          <div className={styles.successIcon}>
            <CheckCircle size={80} />
          </div>
          <h1 className={styles.successTitle}>
            {t('order_confirmed', 'Order Confirmed!')}
          </h1>
          <p className={styles.successSubtitle}>
            {t('order_confirmation_message', 'Thank you for your order. We have received it and will start preparing it shortly.')}
          </p>
          <div className={styles.orderNumber}>
            <Receipt size={24} />
            <div>
              <span className={styles.orderNumberLabel}>
                {t('order_number', 'Order Number')}
              </span>
              <span className={styles.orderNumberValue}>
                {orderNumber || order.orderNumber}
              </span>
            </div>
          </div>
        </div>

        {/* Estimated Time */}
        <div className={styles.estimatedTime}>
          <Clock size={32} className={styles.clockIcon} />
          <div className={styles.timeInfo}>
            <h2>{t('estimated_preparation_time', 'Estimated Preparation Time')}</h2>
            <p className={styles.timeValue}>{getEstimatedTime(order.type)}</p>
          </div>
        </div>

        <div className={styles.gridLayout}>
          {/* Left Column - Order Details */}
          <div className={styles.leftColumn}>
            {/* Order Information */}
            <section className={styles.section}>
              <h2 className={styles.sectionTitle}>
                <ShoppingBag size={20} />
                {t('order_information', 'Order Information')}
              </h2>
              <div className={styles.infoCard}>
                <div className={styles.infoRow}>
                  <span className={styles.infoLabel}>{t('order_type', 'Order Type')}:</span>
                  <span className={styles.infoValue}>{getOrderTypeLabel(order.type)}</span>
                </div>
                <div className={styles.infoRow}>
                  <span className={styles.infoLabel}>{t('status', 'Status')}:</span>
                  <span className={`${styles.infoValue} ${styles.statusBadge}`}>
                    {getStatusLabel(order.status)}
                  </span>
                </div>
                <div className={styles.infoRow}>
                  <span className={styles.infoLabel}>{t('order_date', 'Order Date')}:</span>
                  <span className={styles.infoValue}>{formatDate(order.orderDate)}</span>
                </div>
                {order.tableNumber && (
                  <div className={styles.infoRow}>
                    <span className={styles.infoLabel}>{t('table_number', 'Table Number')}:</span>
                    <span className={styles.infoValue}>{order.tableNumber}</span>
                  </div>
                )}
                {order.deliveryAddress && (
                  <div className={styles.deliveryAddress}>
                    <MapPin size={18} className={styles.addressIcon} />
                    <div>
                      <p className={styles.addressTitle}>{t('delivery_address', 'Delivery Address')}:</p>
                      <p>{order.deliveryAddress.street}</p>
                      <p>{order.deliveryAddress.postalCode} {order.deliveryAddress.city}</p>
                      <p>{order.deliveryAddress.country}</p>
                      {order.deliveryAddress.additionalInfo && (
                        <p className={styles.additionalInfo}>{order.deliveryAddress.additionalInfo}</p>
                      )}
                    </div>
                  </div>
                )}
              </div>
            </section>

            {/* Customer Information */}
            <section className={styles.section}>
              <h2 className={styles.sectionTitle}>
                <User size={20} />
                {t('customer_information', 'Customer Information')}
              </h2>
              <div className={styles.infoCard}>
                {order.customerName && (
                  <div className={styles.infoRow}>
                    <span className={styles.infoLabel}>
                      <User size={16} />
                      {t('name', 'Name')}:
                    </span>
                    <span className={styles.infoValue}>{order.customerName}</span>
                  </div>
                )}
                {order.customerEmail && (
                  <div className={styles.infoRow}>
                    <span className={styles.infoLabel}>
                      <Mail size={16} />
                      {t('email', 'Email')}:
                    </span>
                    <span className={styles.infoValue}>{order.customerEmail}</span>
                  </div>
                )}
                {order.customerPhone && (
                  <div className={styles.infoRow}>
                    <span className={styles.infoLabel}>
                      <Phone size={16} />
                      {t('phone', 'Phone')}:
                    </span>
                    <span className={styles.infoValue}>{order.customerPhone}</span>
                  </div>
                )}
              </div>
            </section>

            {/* Order Items */}
            <section className={styles.section}>
              <h2 className={styles.sectionTitle}>
                <Package size={20} />
                {t('order_items', 'Order Items')} ({order.items.length})
              </h2>
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
                      <h3 className={styles.itemName}>{item.productName}</h3>
                      {item.variationName && (
                        <p className={styles.itemVariation}>{item.variationName}</p>
                      )}
                      {item.specialInstructions && (
                        <p className={styles.itemInstructions}>
                          <i>{item.specialInstructions}</i>
                        </p>
                      )}
                      <p className={styles.itemQuantity}>
                        {t('quantity', 'Qty')}: {item.quantity} × {formatPrice(item.unitPrice)}
                      </p>
                    </div>
                    <div className={styles.itemPrice}>
                      {formatPrice(item.itemTotal)}
                    </div>
                  </div>
                ))}
              </div>
            </section>

            {/* Special Instructions */}
            {order.notes && (
              <section className={styles.section}>
                <h2 className={styles.sectionTitle}>
                  {t('special_instructions', 'Special Instructions')}
                </h2>
                <div className={styles.infoCard}>
                  <p className={styles.notes}>{order.notes}</p>
                </div>
              </section>
            )}
          </div>

          {/* Right Column - Order Summary */}
          <div className={styles.rightColumn}>
            <div className={styles.summaryCard}>
              <h2 className={styles.summaryTitle}>{t('order_summary', 'Order Summary')}</h2>
              
              <div className={styles.summaryRows}>
                <div className={styles.summaryRow}>
                  <span>{t('subtotal', 'Subtotal')}</span>
                  <span>{formatPrice(order.subTotal)}</span>
                </div>
                
                {order.discount > 0 && (
                  <div className={`${styles.summaryRow} ${styles.discount}`}>
                    <span>{t('discount', 'Discount')}</span>
                    <span>-{formatPrice(order.discount)}</span>
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

                {order.tip > 0 && (
                  <div className={styles.summaryRow}>
                    <span>{t('tip', 'Tip')}</span>
                    <span>{formatPrice(order.tip)}</span>
                  </div>
                )}
              </div>
              
              <div className={styles.summaryTotal}>
                <span>{t('total', 'Total')}</span>
                <span className={styles.totalAmount}>
                  {formatPrice(order.total)}
                </span>
              </div>

              {/* Payment Information */}
              {order.payments && order.payments.length > 0 && (
                <div className={styles.paymentInfo}>
                  <h3 className={styles.paymentTitle}>{t('payment_information', 'Payment Information')}</h3>
                  {order.payments.map((payment) => (
                    <div key={payment.id} className={styles.paymentRow}>
                      <span>{payment.paymentMethod}</span>
                      <span className={styles.paymentStatus}>{payment.status}</span>
                    </div>
                  ))}
                </div>
              )}

              {/* Action Buttons */}
              <div className={styles.actions}>
                <button
                  onClick={() => router.push('/orders')}
                  className={styles.trackButton}
                >
                  <Receipt size={20} />
                  {t('track_order', 'Track Order')}
                </button>
                <button
                  onClick={() => router.push('/menu')}
                  className={styles.menuButton}
                >
                  <Home size={20} />
                  {t('back_to_menu', 'Back to Menu')}
                </button>
              </div>

              {/* Additional Information */}
              <div className={styles.helpText}>
                <p>
                  {t('confirmation_email_sent', 'A confirmation email has been sent to')} {order.customerEmail}
                </p>
                <p className={styles.helpNote}>
                  {t('help_text', 'If you have any questions, please contact us.')}
                </p>
              </div>
            </div>
          </div>
        </div>
      </div>
    </main>
  );
}

export default function ConfirmationPage() {
  return (
    <Suspense fallback={
      <main className={styles.container}>
        <div className={styles.loadingState}>
          <Loader2 size={64} className={styles.spinner} />
          <p>Loading...</p>
        </div>
      </main>
    }>
      <ConfirmationContent />
    </Suspense>
  );
}
