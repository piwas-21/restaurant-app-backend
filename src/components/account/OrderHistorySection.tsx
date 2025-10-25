"use client";

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import styles from '../../app/styles/AccountPage.module.css';
import type { OrderHistoryItem, OrderStatus } from '../../app/account/page';

interface OrderHistorySectionProps {
  orderHistory: OrderHistoryItem[];
  getOrderStatusTranslationKey: (status: OrderStatus) => string;
}

export default function OrderHistorySection({ orderHistory, getOrderStatusTranslationKey }: OrderHistorySectionProps) {
  const { t, i18n } = useTranslation(); // Get i18n instance for language
  const [expandedOrderId, setExpandedOrderId] = useState<string | null>(null);
  const [isClient, setIsClient] = useState(false);

  useEffect(() => {
    setIsClient(true);
  }, []);

  const toggleOrderDetails = (orderId: string) => {
    setExpandedOrderId(prevId => (prevId === orderId ? null : orderId));
  };

  const formatDate = (dateString: string) => {
    if (!isClient) {
      // Render a consistent, non-locale-specific format during SSR and initial hydration
      return dateString; // e.g., "2023-10-25"
    }
    // Once mounted on client, use locale-specific formatting, ideally tied to i18n language
    try {
        return new Date(dateString).toLocaleDateString(i18n.language, {
            year: 'numeric', month: '2-digit', day: '2-digit'
        });
    } catch {
        // Fallback if i18n.language is not a valid locale for toLocaleDateString
        return new Date(dateString).toLocaleDateString(undefined, {
            year: 'numeric', month: '2-digit', day: '2-digit'
        });
    }
  };

  return (
    <section className={styles.section}>
      <h2 className={styles.sectionTitle}>{t('order_history_title', 'Order History')}</h2>
      {orderHistory.length === 0 ? (
        <p>{t('no_orders_history_message', 'You have no past orders to display.')}</p>
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
  );
}
