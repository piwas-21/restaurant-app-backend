'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import { OrderDto } from '@/types/order';
import {
  ShoppingBag,
  Clock,
  TrendingUp,
  DollarSign,
} from 'lucide-react';
import styles from './OrderAnalytics.module.css';

interface OrderAnalyticsProps {
  orders: OrderDto[];
}

export default function OrderAnalytics({ orders }: OrderAnalyticsProps) {
  const { t } = useTranslation();
  const formatPrice = (price: number) => {
    return new Intl.NumberFormat('de-CH', {
      style: 'currency',
      currency: 'CHF',
    }).format(price);
  };

  // Get today's date range
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const todayEnd = new Date(today);
  todayEnd.setHours(23, 59, 59, 999);

  // Filter today's orders
  const todaysOrders = orders.filter((order) => {
    const orderDate = new Date(order.orderDate);
    return orderDate >= today && orderDate <= todayEnd;
  });

  // Calculate metrics
  const totalOrdersToday = todaysOrders.length;
  const pendingOrders = orders.filter(
    (order) => order.status === 'Pending' || order.status === 'Confirmed'
  ).length;
  const revenueToday = todaysOrders.reduce((sum, order) => sum + order.total, 0);
  const averageOrderValue = totalOrdersToday > 0 ? revenueToday / totalOrdersToday : 0;

  const cards = [
    {
      title: t('total_orders_today', 'Total Orders Today'),
      value: totalOrdersToday.toString(),
      icon: ShoppingBag,
      color: 'blue',
      description: t('orders_placed_today', 'Orders placed today'),
    },
    {
      title: t('pending_orders', 'Pending Orders'),
      value: pendingOrders.toString(),
      icon: Clock,
      color: 'yellow',
      description: t('awaiting_processing', 'Awaiting processing'),
    },
    {
      title: t('revenue_today', 'Revenue Today'),
      value: formatPrice(revenueToday),
      icon: TrendingUp,
      color: 'green',
      description: t('total_sales_today', 'Total sales today'),
    },
    {
      title: t('average_order_value', 'Average Order Value'),
      value: formatPrice(averageOrderValue),
      icon: DollarSign,
      color: 'purple',
      description: t('per_order_average', 'Per order average'),
    },
  ];

  return (
    <div className={styles.container}>
      {cards.map((card) => (
        <div key={card.title} className={`${styles.card} ${styles[card.color]}`}>
          <div className={styles.cardIcon}>
            <card.icon size={24} />
          </div>
          <div className={styles.cardContent}>
            <h3 className={styles.cardTitle}>{card.title}</h3>
            <p className={styles.cardValue}>{card.value}</p>
            <p className={styles.cardDescription}>{card.description}</p>
          </div>
        </div>
      ))}
    </div>
  );
}
