import React from 'react';
import { useTranslation } from 'react-i18next';
import { ServerTableDto } from '@/services/serverService';
import styles from './TableCard.module.css';

interface TableCardProps {
  table: ServerTableDto;
  isSelected: boolean;
  onClick: () => void;
}

export default function TableCard({ table, isSelected, onClick }: TableCardProps) {
  const { t } = useTranslation();

  const getStatusClass = () => {
    switch (table.status) {
      case 'available':
        return styles.statusAvailable;
      case 'occupied':
        return styles.statusOccupied;
      case 'reserved':
        return styles.statusReserved;
      case 'closed':
        return styles.statusClosed;
      default:
        return styles.statusAvailable;
    }
  };

  const getStatusLabel = () => {
    switch (table.status) {
      case 'available':
        return t('server.status_available', 'Available');
      case 'occupied':
        return t('server.status_occupied', 'Occupied');
      case 'reserved':
        return t('server.status_reserved', 'Reserved');
      case 'closed':
        return t('server.status_closed', 'Closed');
      default:
        return '';
    }
  };

  const getStatusIcon = () => {
    switch (table.status) {
      case 'available':
        return '🟢';
      case 'occupied':
        return '🔵';
      case 'reserved':
        return '🟡';
      case 'closed':
        return '⚫';
      default:
        return '⚪';
    }
  };

  return (
    <div
      className={`${styles.card} ${getStatusClass()} ${isSelected ? styles.selected : ''}`}
      onClick={onClick}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => e.key === 'Enter' && onClick()}
    >
      <div className={styles.tableNumber}>
        <span className={styles.number}>{table.tableNumber}</span>
        {table.isOutdoor && <span className={styles.outdoorBadge}>🌳</span>}
      </div>

      <div className={styles.statusBadge}>
        <span className={styles.statusIcon}>{getStatusIcon()}</span>
        <span className={styles.statusText}>{getStatusLabel()}</span>
      </div>

      {table.hasActiveOrders && (
        <div className={styles.orderCount}>
          <span className={styles.orderBadge}>{table.orderCount}</span>
          <span className={styles.orderLabel}>
            {table.orderCount === 1
              ? t('server.order', 'order')
              : t('server.orders', 'orders')}
          </span>
        </div>
      )}

      {table.upcomingReservation && table.status === 'reserved' && (
        <div className={styles.reservationInfo}>
          <span className={styles.reservationTime}>
            {table.upcomingReservation.startTime.substring(0, 5)}
          </span>
          <span className={styles.reservationGuests}>
            {table.upcomingReservation.numberOfGuests} {t('server.guests', 'guests')}
          </span>
        </div>
      )}

      <div className={styles.capacity}>
        <span>{table.maxGuests}</span>
        <span className={styles.capacityIcon}>👥</span>
      </div>
    </div>
  );
}
