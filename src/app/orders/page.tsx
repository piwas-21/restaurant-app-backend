"use client";

import React, { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/components/AuthContext';
import { getOrders } from '@/services/orderService';
import { addItemToBasket } from '@/services/basketService';
import { useCart } from '@/components/cart/CartContext';
import { OrderDto, OrderStatus } from '@/types/order';
import { useSnackbar } from 'notistack';
import {
  Package,
  Clock,
  MapPin,
  ShoppingBag,
  Filter,
  ChevronDown,
  ChevronRight,
  RefreshCw,
  Loader2,
  AlertCircle,
  CheckCircle,
  Receipt,
  Store,
  Truck,
  UtensilsCrossed,
} from 'lucide-react';
import Image from 'next/image';
import styles from '../styles/OrdersPage.module.css';

export default function OrdersPage() {
  const { t } = useTranslation();
  const router = useRouter();
  const { user, isLoading: authLoading } = useAuth();
  const { enqueueSnackbar } = useSnackbar();
  const { addItem } = useCart();

  const [orders, setOrders] = useState<OrderDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState('');
  const [selectedStatus, setSelectedStatus] = useState<OrderStatus | 'All'>('All');
  const [expandedOrderId, setExpandedOrderId] = useState<string | null>(null);
  const [reorderingOrderId, setReorderingOrderId] = useState<string | null>(null);

  const fetchOrders = useCallback(async () => {
    try {
      setIsLoading(true);
      setError('');

      const filters = selectedStatus !== 'All' ? { status: selectedStatus } : {};
      const result = await getOrders(filters);

      // Sort by date (newest first)
      const sortedOrders = result.items.sort((a, b) =>
        new Date(b.orderDate).getTime() - new Date(a.orderDate).getTime()
      );

      setOrders(sortedOrders);
    } catch (err) {
      // eslint-disable-next-line no-console
      console.error('Error fetching orders:', err);
      setError(t('failed_to_load_orders', 'Failed to load orders'));
      enqueueSnackbar(t('failed_to_load_orders', 'Failed to load orders'), {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    } finally {
      setIsLoading(false);
    }
  }, [selectedStatus, t, enqueueSnackbar]);

  useEffect(() => {
    // Wait for auth to finish loading before checking user status
    if (authLoading) {
      return;
    }

    if (!user) {
      router.push('/login');
      return;
    }

    fetchOrders();
  }, [authLoading, user, router, fetchOrders]);

  const handleReorder = async (order: OrderDto) => {
    try {
      setReorderingOrderId(order.id);

      // Add each item from the order to the basket
      for (const item of order.items) {
        if (item.productId) {
          await addItem({
            productId: item.productId,
            productVariationId: item.productVariationId,
            menuId: item.menuId,
            quantity: item.quantity,
            specialInstructions: item.specialInstructions,
          });
        }
      }

      enqueueSnackbar(
        t('items_added_to_cart', `${order.items.length} items added to cart`),
        {
          variant: 'success',
          anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
        }
      );

      // Navigate to cart
      router.push('/cart');
    } catch (err) {
      // eslint-disable-next-line no-console
      console.error('Error reordering:', err);
      enqueueSnackbar(t('failed_to_reorder', 'Failed to add items to cart'), {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    } finally {
      setReorderingOrderId(null);
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

  const getOrderTypeIcon = (orderType: string) => {
    switch (orderType) {
      case 'DineIn':
        return <UtensilsCrossed size={18} />;
      case 'Takeaway':
        return <Store size={18} />;
      case 'Delivery':
        return <Truck size={18} />;
      default:
        return <ShoppingBag size={18} />;
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

  const getStatusColor = (status: string) => {
    switch (status) {
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

  const statusOptions: (OrderStatus | 'All')[] = [
    'All',
    'Pending',
    'Confirmed',
    'Preparing',
    'Ready',
    'InTransit',
    'Delivered',
    'Completed',
    'Cancelled',
  ];

  if (authLoading || isLoading) {
    return (
      <main className={styles.container}>
        <div className={styles.loadingState}>
          <Loader2 size={64} className={styles.spinner} />
          <p>{authLoading ? t('authenticating', 'Authenticating...') : t('loading_orders', 'Loading your orders...')}</p>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.container}>
      <div className={styles.content}>
        {/* Header */}
        <div className={styles.header}>
          <div className={styles.titleSection}>
            <h1 className={styles.title}>
              <Package size={32} />
              {t('my_orders', 'My Orders')}
            </h1>
            <p className={styles.subtitle}>
              {t('my_orders_desc', 'View and manage your order history')}
            </p>
          </div>
          <button onClick={fetchOrders} className={styles.refreshButton} title={t('refresh', 'Refresh')}>
            <RefreshCw size={20} />
            {t('refresh', 'Refresh')}
          </button>
        </div>

        {/* Filters */}
        <div className={styles.filters}>
          <div className={styles.filterGroup}>
            <Filter size={18} />
            <label htmlFor="status-filter">{t('filter_by_status', 'Filter by Status')}:</label>
            <select
              id="status-filter"
              value={selectedStatus}
              onChange={(e) => setSelectedStatus(e.target.value as OrderStatus | 'All')}
              className={styles.filterSelect}
            >
              {statusOptions.map((status) => (
                <option key={status} value={status}>
                  {status === 'All' ? t('all_orders', 'All Orders') : getStatusLabel(status)}
                </option>
              ))}
            </select>
          </div>
          <div className={styles.orderCount}>
            {orders.length} {orders.length === 1 ? t('order', 'Order') : t('orders', 'Orders')}
          </div>
        </div>

        {/* Error State */}
        {error && (
          <div className={styles.errorAlert}>
            <AlertCircle size={20} />
            <p>{error}</p>
          </div>
        )}

        {/* Orders List */}
        {orders.length === 0 ? (
          <div className={styles.emptyState}>
            <Package size={64} className={styles.emptyIcon} />
            <h2>{t('no_orders_found', 'No Orders Found')}</h2>
            <p>
              {selectedStatus === 'All'
                ? t('no_orders_yet', "You haven't placed any orders yet")
                : t('no_orders_with_status', `No orders with status: ${getStatusLabel(selectedStatus)}`)}
            </p>
            <button onClick={() => router.push('/menu')} className={styles.browseButton}>
              {t('browse_menu', 'Browse Menu')}
            </button>
          </div>
        ) : (
          <div className={styles.ordersList}>
            {orders.map((order) => (
              <div key={order.id} className={styles.orderCard}>
                {/* Order Header */}
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
                    <span className={`${styles.statusBadge} ${getStatusColor(order.status)}`}>
                      {getStatusLabel(order.status)}
                    </span>
                    <button
                      onClick={() => setExpandedOrderId(expandedOrderId === order.id ? null : order.id)}
                      className={styles.expandButton}
                      aria-label={expandedOrderId === order.id ? t('collapse', 'Collapse') : t('expand', 'Expand')}
                    >
                      {expandedOrderId === order.id ? <ChevronDown size={20} /> : <ChevronRight size={20} />}
                    </button>
                  </div>
                </div>

                {/* Order Summary */}
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

                {/* Expanded Details */}
                {expandedOrderId === order.id && (
                  <div className={styles.orderDetails}>
                    {/* Delivery Address or Table */}
                    {order.type === 'Delivery' && order.deliveryAddress && (
                      <div className={styles.detailSection}>
                        <h4 className={styles.detailTitle}>
                          <MapPin size={16} />
                          {t('delivery_address', 'Delivery Address')}
                        </h4>
                        <div className={styles.addressBox}>
                          <p>{order.deliveryAddress.addressLine1}</p>
                          {order.deliveryAddress.addressLine2 && (
                            <p>{order.deliveryAddress.addressLine2}</p>
                          )}
                          <p>{order.deliveryAddress.postalCode} {order.deliveryAddress.city}</p>
                          {order.deliveryAddress.state && (
                            <p>{order.deliveryAddress.state}</p>
                          )}
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

                    {/* Order Items */}
                    <div className={styles.detailSection}>
                      <h4 className={styles.detailTitle}>
                        <Package size={16} />
                        {t('order_items', 'Order Items')}
                      </h4>
                      <div className={styles.itemsList}>
                        {order.items.map((item) => (
                          <div key={item.id} className={styles.orderItem}>
                            {item.productImageUrl && (
                              <div className={styles.itemImage}>
                                <Image
                                  src={item.productImageUrl}
                                  alt={item.productName || ''}
                                  width={50}
                                  height={50}
                                  style={{ objectFit: 'cover' }}
                                />
                              </div>
                            )}
                            <div className={styles.itemDetails}>
                              <h5 className={styles.itemName}>{item.productName}</h5>
                              {item.variationName && (
                                <p className={styles.itemVariation}>{item.variationName}</p>
                              )}
                              {item.specialInstructions && (
                                <p className={styles.itemInstructions}>
                                  <i>{item.specialInstructions}</i>
                                </p>
                              )}
                            </div>
                            <div className={styles.itemQuantity}>
                              <span>×{item.quantity}</span>
                            </div>
                            <div className={styles.itemPrice}>
                              {formatPrice(item.itemTotal)}
                            </div>
                          </div>
                        ))}
                      </div>
                    </div>

                    {/* Price Breakdown */}
                    <div className={styles.detailSection}>
                      <h4 className={styles.detailTitle}>{t('price_breakdown', 'Price Breakdown')}</h4>
                      <div className={styles.priceBreakdown}>
                        <div className={styles.priceRow}>
                          <span>{t('subtotal', 'Subtotal')}</span>
                          <span>{formatPrice(order.subTotal)}</span>
                        </div>
                        {order.discount > 0 && (
                          <div className={`${styles.priceRow} ${styles.discount}`}>
                            <span>{t('discount', 'Discount')}</span>
                            <span>-{formatPrice(order.discount)}</span>
                          </div>
                        )}
                        {order.deliveryFee > 0 && (
                          <div className={styles.priceRow}>
                            <span>{t('delivery_fee', 'Delivery Fee')}</span>
                            <span>{formatPrice(order.deliveryFee)}</span>
                          </div>
                        )}
                        <div className={styles.priceRow}>
                          <span>{t('tax', 'Tax')}</span>
                          <span>{formatPrice(order.tax)}</span>
                        </div>
                        {order.tip > 0 && (
                          <div className={styles.priceRow}>
                            <span>{t('tip', 'Tip')}</span>
                            <span>{formatPrice(order.tip)}</span>
                          </div>
                        )}
                        <div className={`${styles.priceRow} ${styles.total}`}>
                          <span>{t('total', 'Total')}</span>
                          <span>{formatPrice(order.total)}</span>
                        </div>
                      </div>
                    </div>

                    {/* Special Instructions */}
                    {order.notes && (
                      <div className={styles.detailSection}>
                        <h4 className={styles.detailTitle}>{t('special_instructions', 'Special Instructions')}</h4>
                        <p className={styles.notes}>{order.notes}</p>
                      </div>
                    )}

                    {/* Action Buttons */}
                    <div className={styles.detailActions}>
                      <button
                        onClick={() => router.push(`/checkout/confirmation?orderId=${order.id}&orderNumber=${order.orderNumber}`)}
                        className={styles.viewDetailsButton}
                      >
                        <Receipt size={18} />
                        {t('view_details', 'View Details')}
                      </button>
                      <button
                        onClick={() => handleReorder(order)}
                        disabled={reorderingOrderId === order.id}
                        className={styles.reorderButton}
                      >
                        {reorderingOrderId === order.id ? (
                          <>
                            <Loader2 size={18} className={styles.spinner} />
                            {t('adding_to_cart', 'Adding...')}
                          </>
                        ) : (
                          <>
                            <RefreshCw size={18} />
                            {t('reorder', 'Re-order')}
                          </>
                        )}
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </main>
  );
}
