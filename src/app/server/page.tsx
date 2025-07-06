"use client";

import React, { useState } from 'react';
import Link from 'next/link';
import { useTranslation } from 'react-i18next';
import styles from '../styles/ServerPage.module.css';

export type ServerOrderItemStatus = "PENDING" | "IN_KITCHEN" | "READY" | "SERVED_TO_TABLE";
export type ServerOrderStatus = "ACTIVE" | "ALL_ITEMS_SERVED" | "PAID" | "CANCELLED";

export interface ServerOrderItem {
  id: string;
  menuItemId: string;
  name: string;
  quantity: number;
  status: ServerOrderItemStatus;
  notes?: string;
}

export interface ServerOrder {
  id: string;
  tableNumber: string;
  items: ServerOrderItem[];
  orderStatus: ServerOrderStatus;
  timestamp: Date;
  orderNotes?: string;
}

const initialServerOrders: ServerOrder[] = [
    {
    id: "SERV001",
    tableNumber: "3A",
    timestamp: new Date(Date.now() - 3600000 * 0.25),
    orderNotes: "Birthday celebration, bring dessert with candle.",
    items: [
      { id: "sitem001a", menuItemId: "adana01", name: "Adana Kebab", quantity: 1, status: "READY", notes: "Well done" },
      { id: "sitem001b", menuItemId: "sarma01", name: "Sarma (Vegan)", quantity: 2, status: "IN_KITCHEN" },
      { id: "sitem001c", menuItemId: "ayran01", name: "Ayran", quantity: 1, status: "READY" },
    ],
    orderStatus: "ACTIVE",
  },
  {
    id: "SERV002",
    tableNumber: "5B",
    timestamp: new Date(Date.now() - 3600000 * 0.15),
    items: [
      { id: "sitem002a", menuItemId: "pide01", name: "Pide with Cheese", quantity: 1, status: "IN_KITCHEN" },
      { id: "sitem002b", menuItemId: "cola01", name: "Coca-Cola", quantity: 2, status: "READY" },
    ],
    orderStatus: "ACTIVE",
  },
  {
    id: "SERV003",
    tableNumber: "1C",
    timestamp: new Date(Date.now() - 3600000 * 0.05),
    items: [
      { id: "sitem003a", menuItemId: "mixedgrill01", name: "Mixed Grill Platter", quantity: 1, status: "SERVED_TO_TABLE" },
      { id: "sitem003b", menuItemId: "water01", name: "Sparkling Water", quantity: 1, status: "SERVED_TO_TABLE" },
    ],
    orderStatus: "ALL_ITEMS_SERVED",
  },
];

export default function ServerPage() {
  const { t } = useTranslation();
  const [orders, setOrders] = useState<ServerOrder[]>(initialServerOrders.sort((a,b) => a.timestamp.getTime() - b.timestamp.getTime()));

  const handleMarkItemServed = (orderId: string, itemId: string) => {
    setOrders(prevOrders =>
      prevOrders.map(order => {
        if (order.id === orderId) {
          const updatedItems = order.items.map(item =>
            item.id === itemId ? { ...item, status: "SERVED_TO_TABLE" as ServerOrderItemStatus } : item
          );
          const allItemsServed = updatedItems.every(item => item.status === "SERVED_TO_TABLE");
          return {
            ...order,
            items: updatedItems,
            orderStatus: allItemsServed ? "ALL_ITEMS_SERVED" : "ACTIVE",
          };
        }
        return order;
      })
    );
  };

  const getItemStatusStyle = (status: ServerOrderItemStatus) => {
    if (status === "IN_KITCHEN") return styles.statusInKitchen;
    if (status === "READY") return styles.statusReadyForPickup;
    if (status === "SERVED_TO_TABLE") return styles.statusServedToTable;
    return "";
  };
  
  const getOrderStatusStyle = (status: ServerOrderStatus) => {
    if (status === "ACTIVE") return styles.orderStatusActive;
    if (status === "ALL_ITEMS_SERVED") return styles.orderStatusAllServed;
    return "";
  }

  const activeTableOrders = orders.filter(order => order.orderStatus === "ACTIVE");

  return (
    <main className={styles.container}>
      <header className={styles.header}>
        <h1>{t('server_interface_title', 'Server Interface')}</h1>
      </header>

      <h2>{t('active_table_orders_title', 'Active Table Orders')}</h2>
      {activeTableOrders.length === 0 ? (
        <p className={styles.noOrdersMessage}>{t('no_active_table_orders_message', 'No active table orders at the moment.')}</p>
      ) : (
        <div className={styles.ordersGrid}>
          {activeTableOrders.map(order => (
            <div key={order.id} className={`${styles.orderCard} ${order.orderStatus === "ALL_ITEMS_SERVED" ? styles.orderCardAllServed : ''}`}>
              <div className={styles.orderCardHeader}>
                <h3>{t('table_number_label', 'Table')}: {order.tableNumber}</h3>
                <span className={styles.orderTimestamp}>{t('order_timestamp_label', 'Ordered At')}: {order.timestamp.toLocaleTimeString()}</span>
              </div>
              <div className={`${styles.orderStatus} ${getOrderStatusStyle(order.orderStatus)}`}>
                {t(order.orderStatus === "ACTIVE" ? 'order_status_active' : 'order_status_all_items_served', order.orderStatus)}
              </div>
              {order.orderNotes && (
                <div className={styles.orderNotes}>
                    <strong>{t('order_notes_label', 'Notes')}:</strong> {order.orderNotes}
                </div>
              )}
              <ul className={styles.itemList}>
                {order.items.map(item => (
                  <li key={item.id} className={styles.itemEntry}>
                    <span className={styles.itemName}>{item.name}</span>
                    <span className={styles.itemQuantity}>{item.quantity}</span>
                    <div className={styles.itemStatusOrAction}>
                      {item.status === "READY" && (
                        <button 
                            className={styles.actionButton}
                            onClick={() => handleMarkItemServed(order.id, item.id)}
                        >
                            {t('mark_item_served_button', 'Mark Served')}
                        </button>
                      )}
                      {item.status !== "READY" && (
                        <span className={`${styles.itemStatusText} ${getItemStatusStyle(item.status)}`}>
                            {t(`item_status_${item.status.toLowerCase().replace(/\s+/g, '')}`, item.status)}
                        </span>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}
    </main>
  );
}
