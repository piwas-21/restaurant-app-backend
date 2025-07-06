"use client";

import React, { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import styles from '../styles/KitchenStaffPage.module.css';
import categoriesData from '../../data/categories.json';
import type { LanguageCode } from '@/components/LanguageSwitcher';

export type KitchenOrderItemStatus = "Pending" | "Preparing" | "Ready";

export interface KitchenOrderItem {
  id: string;
  menuItemId: string;
  name: string;
  quantity: number;
  status: KitchenOrderItemStatus;
  categoryKey: keyof typeof categoriesData.en;
  notes?: string;
}

export interface KitchenOrder {
  id: string;
  orderType: 'Pickup' | 'Dine-in';
  tableNumber?: string;
  items: KitchenOrderItem[];
  timestamp: Date;
  orderNotes?: string;
}

interface CategoryData {
  [language: string]: {
    [categoryKey: string]: string;
  };
}

const categories: CategoryData = categoriesData;

const initialKitchenOrders: KitchenOrder[] = [
    {
    id: "KORD001",
    orderType: "Dine-in",
    tableNumber: "3A",
    timestamp: new Date(Date.now() - 3600000 * 0.2),
    orderNotes: "Customer is in a hurry.",
    items: [
      { id: "kitem001a", menuItemId: "adana01", name: "Adana Kebab", quantity: 1, status: "Pending", categoryKey: "grill", notes: "Well done" },
      { id: "kitem001b", menuItemId: "sarma01", name: "Sarma (Vegan)", quantity: 2, status: "Pending", categoryKey: "starters" },
    ],
  },
  {
    id: "KORD002",
    orderType: "Pickup",
    timestamp: new Date(Date.now() - 3600000 * 0.1),
    items: [
      { id: "kitem002a", menuItemId: "pide01", name: "Pide with Cheese", quantity: 1, status: "Pending", categoryKey: "pide" },
      { id: "kitem002b", menuItemId: "pide02", name: "Pide with Minced Lamb", quantity: 1, status: "Preparing", categoryKey: "pide" },
      { id: "kitem002c", menuItemId: "dessert01", name: "Baklava", quantity: 4, status: "Pending", categoryKey: "dessert" },
    ],
  },
  {
    id: "KORD003",
    orderType: "Dine-in",
    tableNumber: "7",
    timestamp: new Date(Date.now() - 3600000 * 0.05),
    items: [
      { id: "kitem003a", menuItemId: "grill005", name: "Mixed Grill Platter", quantity: 1, status: "Pending", categoryKey: "grill" },
    ],
  },
   {
    id: "KORD004",
    orderType: "Dine-in",
    tableNumber: "9",
    timestamp: new Date(Date.now() - 3600000 * 0.02),
    items: [
      { id: "kitem004a", menuItemId: "pizza01", name: "Margherita Pizza", quantity: 1, status: "Ready", categoryKey: "pizza" },
      { id: "kitem004b", menuItemId: "coldDrink02", name: "Cola", quantity: 2, status: "Ready", categoryKey: "coldDrink" },

    ],
  },
];

interface CategoryForFilter {
  key: keyof typeof categoriesData.en;
  name: string;
}

export default function KitchenStaffPage() {
  const { t, i18n } = useTranslation();
  const [orders, setOrders] = useState<KitchenOrder[]>(initialKitchenOrders.sort((a,b) => a.timestamp.getTime() - b.timestamp.getTime()));
  const [dishTypeFilter, setDishTypeFilter] = useState<string>("all");
  const [availableCategories, setAvailableCategories] = useState<CategoryForFilter[]>([]);

  const currentLanguage = (i18n.language.split('-')[0] || 'en') as LanguageCode;

  useEffect(() => {
    const cats = Object.keys(categoriesData.en).map(key => ({
      key: key as keyof typeof categoriesData.en,
      name: (categories[currentLanguage]?.[key] || categories.en[key]) as string
    }));
    setAvailableCategories(cats);
  }, [currentLanguage]);

  const handleUpdateItemStatus = (orderId: string, itemId: string, newStatus: KitchenOrderItemStatus) => {
    setOrders(prevOrders =>
      prevOrders.map(order => {
        if (order.id === orderId) {
          return {
            ...order,
            items: order.items.map(item =>
              item.id === itemId ? { ...item, status: newStatus } : item
            ),
          };
        }
        return order;
      })
    );
  };

  const activeOrders = orders.filter(order => {
    const hasNonReadyItems = order.items.some(item => item.status !== "Ready");
    if (!hasNonReadyItems) return false;

    if (dishTypeFilter === "all") return true;
    
    return order.items.some(item => item.categoryKey === dishTypeFilter && item.status !== "Ready");
  });

  const getStatusTextStyle = (status: KitchenOrderItemStatus) => {
    if (status === "Preparing") return styles.itemStatusTextPreparing;
    if (status === "Ready") return styles.itemStatusTextReady;
    return styles.itemStatusTextPending;
  };

  return (
    <main className={styles.container}>
      <header className={styles.header}>
        <h1>{t('kitchen_staff_dashboard_title', 'Kitchen Dashboard')}</h1>
      </header>

      <div className={styles.filterContainer}>
        <label htmlFor="dishTypeFilter">{t('filter_by_dish_type_label', 'Filter by Dish Type:')}</label>
        <select 
            id="dishTypeFilter" 
            className={styles.select} 
            value={dishTypeFilter} 
            onChange={(e) => setDishTypeFilter(e.target.value)}
        >
            <option value="all">{t('all_dish_types_filter', 'All Types')}</option>
            {availableCategories.map(cat => (
                <option key={cat.key} value={cat.key}>{cat.name}</option>
            ))}
        </select>
      </div>

      <h2>{t('orders_to_prepare_title', 'Orders to Prepare')}</h2>
      {activeOrders.length === 0 ? (
        <p className={styles.noOrdersMessage}>{t('no_orders_to_prepare_message', 'No orders currently need preparation.')}</p>
      ) : (
        <div className={styles.ordersGrid}>
          {activeOrders.map(order => (
            <div key={order.id} className={styles.orderCard}>
              <div className={styles.orderCardHeader}>
                <h3>{t('order_id_label', 'Order ID')}: {order.id}</h3>
                <span className={styles.orderInfo}>
                    {t(order.orderType === "Dine-in" ? 'checkout_order_type_dine_in' : 'checkout_order_type_pickup', order.orderType)}
                    {order.orderType === 'Dine-in' && order.tableNumber && ` (${t('table_number_label_short', 'Table')}: ${order.tableNumber})`}
                </span>
              </div>
              {order.orderNotes && (
                <div className={styles.orderNotes}>
                    <strong>{t('order_notes_label', 'Notes')}:</strong> {order.orderNotes}
                </div>
              )}
              <ul className={styles.itemList}>
                {order.items
                  .filter(item => dishTypeFilter === "all" || item.categoryKey === dishTypeFilter)
                  .map(item => (
                  <li key={item.id} className={styles.itemEntry}>
                    <div className={styles.itemName}>
                        {item.name}
                        {item.notes && <div style={{fontSize: '0.8em', color: 'var(--text-secondary)'}}><em>({item.notes})</em></div>}
                    </div>
                    <div className={styles.itemQuantity}>{item.quantity}</div>
                    <div className={styles.itemActions}>
                      {item.status === "Pending" && (
                        <button 
                            className={styles.statusButton}
                            onClick={() => handleUpdateItemStatus(order.id, item.id, "Preparing")}
                        >
                            {t('mark_as_preparing_button', 'Start Preparing')}
                        </button>
                      )}
                      {item.status === "Preparing" && (
                        <button 
                            className={`${styles.statusButton} ${styles.statusPreparing}`}
                            onClick={() => handleUpdateItemStatus(order.id, item.id, "Ready")}
                        >
                            {t('mark_as_ready_button', 'Mark as Ready')}
                        </button>
                      )}
                       {item.status === "Ready" && (
                        <span className={`${styles.itemStatusText} ${getStatusTextStyle(item.status)}`}>
                            {t('item_status_ready', 'Ready')}
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
