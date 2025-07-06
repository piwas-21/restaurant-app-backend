"use client";

import React, { useState } from 'react';
import Link from 'next/link';
import { useTranslation } from 'react-i18next';
import styles from '../styles/CashierPage.module.css';

// Define types for Order and OrderItem
interface OrderItem {
  id: string;
  name: string;
  quantity: number;
  price: number; // Price per unit
}

export type PaymentStatus = "Paid Online" | "To Be Paid at Cashier" | "Paid at Cashier";
export type OrderType = "Pickup" | "Dine-in";

export interface Order {
  id: string;
  customerName: string;
  items: OrderItem[];
  totalPrice: number;
  paymentStatus: PaymentStatus;
  orderType: OrderType;
  tableNumber?: string; // Optional, for Dine-in
  timestamp: Date;
}

// Mock Data - in a real app, this would come from an API
const initialOrders: Order[] = [
  {
    id: "ORD001",
    customerName: "Alice Wonderland",
    items: [
      { id: "item001", name: "Adana Kebab", quantity: 1, price: 23.90 },
      { id: "item002", name: "Ayran", quantity: 2, price: 3.50 },
    ],
    totalPrice: 30.90,
    paymentStatus: "Paid Online",
    orderType: "Pickup",
    timestamp: new Date(Date.now() - 3600000 * 1), // 1 hour ago
  },
  {
    id: "ORD002",
    customerName: "Bob The Builder",
    items: [
      { id: "item003", name: "Pide (Turkish Pizza)", quantity: 2, price: 18.00 },
      { id: "item004", name: "Coca-Cola", quantity: 4, price: 3.00 },
    ],
    totalPrice: 48.00,
    paymentStatus: "To Be Paid at Cashier",
    orderType: "Dine-in",
    tableNumber: "5",
    timestamp: new Date(Date.now() - 3600000 * 0.5), // 30 mins ago
  },
  {
    id: "ORD003",
    customerName: "Charlie Brown",
    items: [
      { id: "item005", name: "Sarma (Vegan)", quantity: 6, price: 1.90 },
      { id: "item006", name: "Turkish Tea", quantity: 1, price: 2.50 },
    ],
    totalPrice: 13.90,
    paymentStatus: "To Be Paid at Cashier",
    orderType: "Pickup",
    timestamp: new Date(Date.now() - 3600000 * 0.2), // 12 mins ago
  },
];

export default function CashierPage() {
  const { t } = useTranslation();
  const [orders, setOrders] = useState<Order[]>(initialOrders.sort((a,b) => b.timestamp.getTime() - a.timestamp.getTime()));
  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null);
  const [filter, setFilter] = useState<'all' | PaymentStatus>('all');

  const handleMarkAsPaid = (orderId: string) => {
    setOrders(prevOrders =>
      prevOrders.map(order =>
        order.id === orderId ? { ...order, paymentStatus: "Paid at Cashier" } : order
      )
    );
  };

  const toggleViewDetails = (orderId: string) => {
    setSelectedOrderId(prevId => (prevId === orderId ? null : orderId));
  };

  const filteredOrders = orders.filter(order => 
    filter === 'all' || order.paymentStatus === filter
  );

  return (
    <main className={styles.container}>
      <header className={styles.header}>
        <h1>{t('cashier_interface_title', 'Cashier Interface')}</h1>
      </header>

      <div className={styles.filterContainer}>
        <label htmlFor="paymentStatusFilter">{t('filter_by_payment_status', 'Filter by Payment Status:')}</label>
        <select 
            id="paymentStatusFilter" 
            className={styles.select} 
            value={filter} 
            onChange={(e) => setFilter(e.target.value as 'all' | PaymentStatus)}
        >
            <option value="all">{t('filter_all', 'All')}</option>
            <option value="Paid Online">{t('payment_status_paid_online', 'Paid Online')}</option>
            <option value="To Be Paid at Cashier">{t('payment_status_to_be_paid', 'To Be Paid at Cashier')}</option>
            <option value="Paid at Cashier">{t('payment_status_paid_at_cashier', 'Paid at Cashier')}</option>
        </select>
      </div>

      <h2>{t('orders_list_title', 'Current Orders')}</h2>
      {filteredOrders.length === 0 ? (
        <p>{t('no_orders_message', 'No orders match the current filter.')}</p>
      ) : (
        <table className={styles.orderTable}>
          <thead>
            <tr>
              <th className={styles.th}>{t('order_id_header', 'Order ID')}</th>
              <th className={styles.th}>{t('customer_name_header', 'Customer')}</th>
              <th className={styles.th}>{t('order_type_header', 'Type')}</th>
              <th className={styles.th}>{t('total_price_header', 'Total')}</th>
              <th className={styles.th}>{t('payment_status_header', 'Payment Status')}</th>
              <th className={styles.th}>{t('actions_header', 'Actions')}</th>
            </tr>
          </thead>
          <tbody>
            {filteredOrders.map(order => (
              <React.Fragment key={order.id}>
                <tr>
                  <td className={styles.td}>{order.id}</td>
                  <td className={styles.td}>{order.customerName}</td>
                  <td className={styles.td}>
                    {t(order.orderType === "Dine-in" ? 'checkout_order_type_dine_in' : 'checkout_order_type_pickup', order.orderType)}
                    {order.orderType === 'Dine-in' && order.tableNumber && ` (${t('table_number_label_short', 'Table')}: ${order.tableNumber})`}
                  </td>
                  <td className={styles.td}>CHF {order.totalPrice.toFixed(2)}</td>
                  <td className={styles.td}>
                    {order.paymentStatus === "Paid Online" && t('payment_status_paid_online', order.paymentStatus)}
                    {order.paymentStatus === "To Be Paid at Cashier" && t('payment_status_to_be_paid', order.paymentStatus)}
                    {order.paymentStatus === "Paid at Cashier" && t('payment_status_paid_at_cashier', order.paymentStatus)}
                  </td>
                  <td className={styles.td}>
                    <button 
                        className={styles.viewDetailsButton} 
                        onClick={() => toggleViewDetails(order.id)}
                    >
                        {selectedOrderId === order.id ? t('hide_details_button', 'Hide Details') : t('view_details_button', 'View Details')}
                    </button>
                    {order.paymentStatus === "To Be Paid at Cashier" && (
                      <button 
                        className={styles.paidButton} 
                        onClick={() => handleMarkAsPaid(order.id)}
                       >
                        {t('mark_as_paid_button', 'Mark as Paid')}
                      </button>
                    )}
                  </td>
                </tr>
                {selectedOrderId === order.id && (
                  <tr>
                    <td colSpan={6} className={styles.orderDetailsSection}>
                      <h3>{t('order_details_title', 'Order Details')} ({order.id})</h3>
                      <p><strong>{t('customer_name_header', 'Customer')}:</strong> {order.customerName}</p>
                      <p><strong>{t('order_type_header', 'Type')}:</strong> 
                        {t(order.orderType === "Dine-in" ? 'checkout_order_type_dine_in' : 'checkout_order_type_pickup', order.orderType)}
                        {order.orderType === 'Dine-in' && order.tableNumber && ` (${t('table_number_label_short', 'Table')}: ${order.tableNumber})`}
                      </p>
                      <p><strong>{t('payment_status_header', 'Payment Status')}:</strong> 
                        {order.paymentStatus === "Paid Online" && t('payment_status_paid_online', order.paymentStatus)}
                        {order.paymentStatus === "To Be Paid at Cashier" && t('payment_status_to_be_paid', order.paymentStatus)}
                        {order.paymentStatus === "Paid at Cashier" && t('payment_status_paid_at_cashier', order.paymentStatus)}
                      </p>
                      <p><strong>{t('items_ordered_label', 'Items:')}</strong></p>
                      <ul>
                        {order.items.map(item => (
                          <li key={item.id}>
                            {item.name} - {t('quantity_short_label', 'Qty')}: {item.quantity} @ CHF {item.price.toFixed(2)} {t('each', 'each')}
                          </li>
                        ))}
                      </ul>
                      <p><strong>{t('checkout_total_label', 'Total')}: CHF {order.totalPrice.toFixed(2)}</strong></p>
                    </td>
                  </tr>
                )}
              </React.Fragment>
            ))}
          </tbody>
        </table>
      )}
    </main>
  );
}
