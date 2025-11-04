"use client";

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { getMyOrders } from '@/services/orderService';
import { OrderDto } from '@/types/order';
import { formatPriceWithRounding, hasActiveDiscount } from '@/utils/priceRounding';
import styles from './MyOrders.module.css';

export type OrderStatus = "Delivered" | "In Progress" | "Cancelled" | "Pending Payment" | "Pending" | "Confirmed" | "Preparing" | "Ready" | "InTransit" | "Completed";

export interface OrderItemDetail {
  id: string;
  name: string;
  quantity: number;
  price: number;
}

export interface OrderHistoryItem {
  id: string;
  date: string;
  status: OrderStatus;
  totalAmount: number;
  orderType: string;
  items: OrderItemDetail[];
  deliveryAddress?: {
    addressLine1: string;
    addressLine2?: string;
    city: string;
    state?: string;
    postalCode: string;
    country: string;
  };
}

export default function MyOrders() {
  const { t, i18n } = useTranslation();
  const [orderHistory, setOrderHistory] = useState<OrderHistoryItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedOrderId, setExpandedOrderId] = useState<string | null>(null);
  const [isClient, setIsClient] = useState(false);

  useEffect(() => {
    setIsClient(true);
  }, []);

  useEffect(() => {
    loadOrders();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const loadOrders = async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await getMyOrders({ pageSize: 50, page: 1 });

      const transformedOrders: OrderHistoryItem[] = result.items.map((order: OrderDto) => ({
        id: order.id,
        date: new Date(order.orderDate).toISOString().split('T')[0],
        status: order.status as OrderStatus,
        totalAmount: order.total,
        orderType: order.type,
        items: order.items.map(item => ({
          id: item.id,
          name: item.productName || item.menuName || 'Unknown Item',
          quantity: item.quantity,
          price: item.unitPrice,
        })),
        deliveryAddress: order.deliveryAddress ? {
          addressLine1: order.deliveryAddress.addressLine1,
          addressLine2: order.deliveryAddress.addressLine2,
          city: order.deliveryAddress.city,
          state: order.deliveryAddress.state,
          postalCode: order.deliveryAddress.postalCode,
          country: order.deliveryAddress.country,
        } : undefined,
      }));

      setOrderHistory(transformedOrders);
    } catch (err: any) {
      setError(err.message || t('failed_to_load_orders', 'Failed to load orders'));
      setOrderHistory([]);
    } finally {
      setLoading(false);
    }
  };

  const toggleOrderDetails = (orderId: string) => {
    setExpandedOrderId(prevId => (prevId === orderId ? null : orderId));
  };

  const formatDate = (dateString: string) => {
    if (!isClient) {
      return dateString;
    }
    try {
      return new Date(dateString).toLocaleDateString(i18n.language, {
        year: 'numeric', month: '2-digit', day: '2-digit'
      });
    } catch {
      return new Date(dateString).toLocaleDateString(undefined, {
        year: 'numeric', month: '2-digit', day: '2-digit'
      });
    }
  };

  const getOrderStatusTranslationKey = (status: OrderStatus): string => {
    switch (status) {
      case "Delivered": return 'order_status_delivered';
      case "In Progress": return 'order_status_in_progress';
      case "Cancelled": return 'order_status_cancelled';
      case "Pending Payment": return 'order_status_pending_payment';
      default: return status;
    }
  };

  if (loading) {
    return (
      <div className={styles.container}>
        <section className={styles.section}>
          <h2 className={styles.sectionTitle}>{t('order_history_title', 'Order History')}</h2>
          <p className={styles.loadingText}>{t('loading', 'Loading...')}</p>
        </section>
      </div>
    );
  }

  if (error) {
    return (
      <div className={styles.container}>
        <section className={styles.section}>
          <h2 className={styles.sectionTitle}>{t('order_history_title', 'Order History')}</h2>
          <p className={styles.error}>{error}</p>
        </section>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <section className={styles.section}>
        <h2 className={styles.sectionTitle}>{t('order_history_title', 'Order History')}</h2>
        {orderHistory.length === 0 ? (
          <p className={styles.emptyMessage}>{t('no_orders_history_message', 'You have no past orders to display.')}</p>
        ) : (
          <ul className={styles.orderHistoryList}>
            {orderHistory.map(order => (
              <React.Fragment key={order.id}>
                <li className={styles.orderHistoryItem}>
                  <div>
                    <span className={styles.orderDetailLabel}>{t('order_date_label', 'Date')}</span>
                    <span className={styles.orderDetailValue}>{formatDate(order.date)}</span>
                  </div>
                  <div>
                    <span className={styles.orderDetailLabel}>{t('order_type_label', 'Type')}</span>
                    <span className={styles.orderDetailValue}>{t(`order_type_${order.orderType.toLowerCase()}`, order.orderType)}</span>
                  </div>
                  <div>
                    <span className={styles.orderDetailLabel}>{t('order_status_label', 'Status')}</span>
                    <span className={styles.orderDetailValue}>{t(getOrderStatusTranslationKey(order.status), order.status)}</span>
                  </div>
                  <div>
                    <span className={styles.orderDetailLabel}>{t('order_total_label', 'Total')}</span>
                    <span className={styles.orderDetailValue}>CHF {order.totalAmount.toFixed(2)}</span>
                  </div>
                  <div>
                    <button
                      onClick={() => toggleOrderDetails(order.id)}
                      className={styles.viewDetailsButton}
                      aria-expanded={expandedOrderId === order.id}
                      aria-controls={`order-details-${order.id}`}
                    >
                      {expandedOrderId === order.id ? t('hide_details_button', 'Hide Details') : t('view_order_details_button', 'View Details')}
                    </button>
                  </div>
                </li>
                {expandedOrderId === order.id && (
                  <li id={`order-details-${order.id}`} className={styles.orderDetailsExpanded}>
                    <h4 className={styles.orderDetailsTitle}>{t('order_items_title', 'Items in this Order')}</h4>
                    {order.items && order.items.length > 0 ? (
                      <table className={styles.orderItemsTable}>
                        <thead>
                          <tr>
                            <th>{t('item_label', 'Item')}</th>
                            <th>{t('quantity_label', 'Quantity')}</th>
                            <th>{t('price_per_item_label', 'Price/Item')}</th>
                            <th>{t('subtotal_label', 'Subtotal')}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {order.items.map(item => (
                            <tr key={item.id}>
                              <td>{item.name}</td>
                              <td>{item.quantity}</td>
                              <td>CHF {item.price.toFixed(2)}</td>
                              <td>CHF {(item.quantity * item.price).toFixed(2)}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    ) : (
                      <p>{t('no_items_in_this_order', 'No item details available for this order.')}</p>
                    )}

                    {order.deliveryAddress && (
                      <div className={styles.deliveryAddressSection}>
                        <h4 className={styles.orderDetailsTitle}>{t('delivery_address_title', 'Delivery Address')}</h4>
                        <div className={styles.addressDetails}>
                          <p>{order.deliveryAddress.addressLine1}</p>
                          {order.deliveryAddress.addressLine2 && <p>{order.deliveryAddress.addressLine2}</p>}
                          <p>
                            {order.deliveryAddress.postalCode} {order.deliveryAddress.city}
                            {order.deliveryAddress.state && `, ${order.deliveryAddress.state}`}
                          </p>
                          <p>{order.deliveryAddress.country}</p>
                        </div>
                      </div>
                    )}
                  </li>
                )}
              </React.Fragment>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
