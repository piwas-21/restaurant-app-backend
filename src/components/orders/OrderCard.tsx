'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import { OrderDto } from '@/types/order';
import {
  Package,
  Clock,
  MapPin,
  ShoppingBag,
  ChevronDown,
  ChevronRight,
  RefreshCw,
  Loader2,
  Receipt,
  Store,
  Truck,
  UtensilsCrossed,
} from 'lucide-react';
import Image from 'next/image';
import { useRouter } from 'next/navigation';
import { formatPrice, formatDate } from './orderFormatters';
import styles from '@/app/styles/OrdersPage.module.css';

interface OrderCardProps {
  order: OrderDto;
  isExpanded: boolean;
  onToggleExpand: (id: string) => void;
  isReordering: boolean;
  onReorder: (order: OrderDto) => void;
}

export default function OrderCard({ order, isExpanded, onToggleExpand, isReordering, onReorder }: OrderCardProps) {
  const { t } = useTranslation();
  const router = useRouter();

  const getOrderTypeIcon = (type: string) => {
    switch (type) {
      case 'DineIn': return <UtensilsCrossed size={18} />;
      case 'Takeaway': return <Store size={18} />;
      case 'Delivery': return <Truck size={18} />;
      default: return <ShoppingBag size={18} />;
    }
  };

  const getOrderTypeLabel = (type: string) => {
    switch (type) {
      case 'DineIn': return t('order_type_dine_in', 'Dine In');
      case 'Takeaway': return t('order_type_takeaway', 'Takeaway');
      case 'Delivery': return t('order_type_delivery', 'Delivery');
      default: return type;
    }
  };

  const getStatusLabel = (status: string) => {
    switch (status) {
      case 'Pending': return t('order_status_pending', 'Pending');
      case 'Confirmed': return t('order_status_confirmed', 'Confirmed');
      case 'Preparing': return t('order_status_preparing', 'Preparing');
      case 'Ready': return t('order_status_ready', 'Ready');
      case 'InTransit': return t('order_status_in_transit', 'In Transit');
      case 'Delivered': return t('order_status_delivered', 'Delivered');
      case 'Completed': return t('order_status_completed', 'Completed');
      case 'Cancelled': return t('order_status_cancelled', 'Cancelled');
      default: return status;
    }
  };

  const getStatusClass = (status: string) => {
    switch (status) {
      case 'Pending': case 'Confirmed': return styles.statusPending;
      case 'Preparing': return styles.statusPreparing;
      case 'Ready': case 'InTransit': return styles.statusReady;
      case 'Delivered': case 'Completed': return styles.statusCompleted;
      case 'Cancelled': return styles.statusCancelled;
      default: return '';
    }
  };

  return (
    <div className={styles.orderCard}>
      <div className={styles.orderHeader}>
        <div className={styles.orderInfo}>
          <div className={styles.orderNumber}>
            <Receipt size={20} />
            <span className={styles.orderNumberLabel}>{t('order', 'Order')} #</span>
            <span className={styles.orderNumberValue}>{order.orderNumber}</span>
          </div>
          <div className={styles.orderMeta}>
            <span className={styles.orderType}>
              {getOrderTypeIcon(order.type)}
              {getOrderTypeLabel(order.type)}
            </span>
            <span className={styles.orderDate}>
              <Clock size={16} />
              {formatDate(order.orderDate)}
            </span>
          </div>
        </div>
        <div className={styles.orderActions}>
          <span className={`${styles.statusBadge} ${getStatusClass(order.status)}`}>
            {getStatusLabel(order.status)}
          </span>
          <button
            onClick={() => onToggleExpand(order.id)}
            className={styles.expandButton}
            aria-label={isExpanded ? t('collapse', 'Collapse') : t('expand', 'Expand')}
          >
            {isExpanded ? <ChevronDown size={20} /> : <ChevronRight size={20} />}
          </button>
        </div>
      </div>

      <div className={styles.orderSummary}>
        <div className={styles.summaryItem}>
          <ShoppingBag size={16} />
          <span>{order.items.length} {order.items.length === 1 ? t('item', 'Item') : t('items', 'Items')}</span>
        </div>
        <div className={styles.summaryDivider}>•</div>
        <div className={styles.summaryItem}>
          <span className={styles.totalLabel}>{t('total', 'Total')}:</span>
          <span className={styles.totalValue}>{formatPrice(order.total)}</span>
        </div>
      </div>

      {isExpanded && (
        <div className={styles.orderDetails}>
          {order.type === 'Delivery' && order.deliveryAddress && (
            <div className={styles.detailSection}>
              <h4 className={styles.detailTitle}><MapPin size={16} />{t('delivery_address', 'Delivery Address')}</h4>
              <div className={styles.addressBox}>
                <p>{order.deliveryAddress.addressLine1}</p>
                {order.deliveryAddress.addressLine2 && <p>{order.deliveryAddress.addressLine2}</p>}
                <p>{order.deliveryAddress.postalCode} {order.deliveryAddress.city}</p>
                {order.deliveryAddress.state && <p>{order.deliveryAddress.state}</p>}
                <p>{order.deliveryAddress.country}</p>
                {order.deliveryAddress.deliveryInstructions && (
                  <p className={styles.additionalInfo}>{order.deliveryAddress.deliveryInstructions}</p>
                )}
              </div>
            </div>
          )}

          {order.type === 'DineIn' && order.tableNumber && (
            <div className={styles.detailSection}>
              <h4 className={styles.detailTitle}>{t('table_number', 'Table Number')}</h4>
              <p className={styles.tableNumber}>{order.tableNumber}</p>
            </div>
          )}

          <div className={styles.detailSection}>
            <h4 className={styles.detailTitle}><Package size={16} />{t('order_items', 'Order Items')}</h4>
            <div className={styles.itemsList}>
              {order.items.map((item) => (
                <div key={item.id} className={styles.orderItem}>
                  {item.productImageUrl && (
                    <div className={styles.itemImage}>
                      <Image src={item.productImageUrl} alt={item.productName || ''} width={50} height={50} style={{ objectFit: 'cover' }} />
                    </div>
                  )}
                  <div className={styles.itemDetails}>
                    <h5 className={styles.itemName}>{item.productName}</h5>
                    {item.variationName && <p className={styles.itemVariation}>{item.variationName}</p>}
                    {item.specialInstructions && <p className={styles.itemInstructions}><i>{item.specialInstructions}</i></p>}
                  </div>
                  <div className={styles.itemQuantity}><span>×{item.quantity}</span></div>
                  <div className={styles.itemPrice}>{formatPrice(item.itemTotal)}</div>
                </div>
              ))}
            </div>
          </div>

          <div className={styles.detailSection}>
            <h4 className={styles.detailTitle}>{t('price_breakdown', 'Price Breakdown')}</h4>
            <div className={styles.priceBreakdown}>
              <div className={styles.priceRow}><span>{t('subtotal', 'Subtotal')}</span><span>{formatPrice(order.subTotal)}</span></div>
              {order.discount > 0 && (
                <div className={`${styles.priceRow} ${styles.discount}`}><span>{t('discount', 'Discount')}</span><span>-{formatPrice(order.discount)}</span></div>
              )}
              {order.deliveryFee > 0 && (
                <div className={styles.priceRow}><span>{t('delivery_fee', 'Delivery Fee')}</span><span>{formatPrice(order.deliveryFee)}</span></div>
              )}
              <div className={styles.priceRow}><span>{t('tax', 'Tax')}</span><span>{formatPrice(order.tax)}</span></div>
              {order.tip > 0 && (
                <div className={styles.priceRow}><span>{t('tip', 'Tip')}</span><span>{formatPrice(order.tip)}</span></div>
              )}
              <div className={`${styles.priceRow} ${styles.total}`}><span>{t('total', 'Total')}</span><span>{formatPrice(order.total)}</span></div>
            </div>
          </div>

          {order.notes && (
            <div className={styles.detailSection}>
              <h4 className={styles.detailTitle}>{t('special_instructions', 'Special Instructions')}</h4>
              <p className={styles.notes}>{order.notes}</p>
            </div>
          )}

          <div className={styles.detailActions}>
            <button
              onClick={() => router.push(`/checkout/confirmation?orderId=${order.id}&orderNumber=${order.orderNumber}`)}
              className={styles.viewDetailsButton}
            >
              <Receipt size={18} />{t('view_details', 'View Details')}
            </button>
            <button onClick={() => onReorder(order)} disabled={isReordering} className={styles.reorderButton}>
              {isReordering
                ? <><Loader2 size={18} className={styles.spinner} />{t('adding_to_cart', 'Adding...')}</>
                : <><RefreshCw size={18} />{t('reorder', 'Re-order')}</>}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
