import React from 'react';
import { OrderType } from '@/types/order';
import styles from './OrderTypeNav.module.css';

interface OrderTypeNavProps {
  activeFilter: string;
  onFilterChange: (filter: string) => void;
}

export default function OrderTypeNav({ activeFilter, onFilterChange }: OrderTypeNavProps) {
  return (
    <div className={styles.nav}>
      <button
        className={`${styles.tab} ${activeFilter === 'all' ? styles.tabActive : ''}`}
        onClick={() => onFilterChange('all')}
      >
        All Orders
      </button>
      <button
        className={`${styles.tab} ${activeFilter === OrderType.DineIn ? styles.tabActive : ''}`}
        onClick={() => onFilterChange(OrderType.DineIn)}
      >
        Dine In
      </button>
      <button
        className={`${styles.tab} ${activeFilter === OrderType.Takeaway ? styles.tabActive : ''}`}
        onClick={() => onFilterChange(OrderType.Takeaway)}
      >
        Takeaway
      </button>
      <button
        className={`${styles.tab} ${activeFilter === OrderType.Delivery ? styles.tabActive : ''}`}
        onClick={() => onFilterChange(OrderType.Delivery)}
      >
        Delivery
      </button>
    </div>
  );
}
