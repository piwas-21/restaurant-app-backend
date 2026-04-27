import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ServerTableDto, closeTable, openTable, releaseTable, completeAllTableOrders } from '@/services/serverService';
import { OrderDto } from '@/types/order';
import styles from './TableDetailsModal.module.css';

interface TableDetailsModalProps {
  table: ServerTableDto;
  orders: OrderDto[];
  onClose: () => void;
  onUpdateOrderStatus: (orderId: string, status: string) => void;
  onTakeOrder: () => void;
  onTableStatusChanged: () => void;
}

export default function TableDetailsModal({
  table,
  orders,
  onClose,
  onUpdateOrderStatus,
  onTakeOrder,
  onTableStatusChanged,
}: TableDetailsModalProps) {
  const { t } = useTranslation();
  const [isUpdating, setIsUpdating] = useState(false);
  const [error, setError] = useState<string | null>(null);

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

  const activeOrders = orders.filter((order) => !['Completed', 'Cancelled'].includes(order.status));

  const handleCloseTable = async () => {
    try {
      setIsUpdating(true);
      setError(null);
      await closeTable(table.id);
      onTableStatusChanged();
      onClose();
    } catch (err) {
      console.error('Failed to close table:', err);
      setError(err instanceof Error ? err.message : 'Failed to close table');
    } finally {
      setIsUpdating(false);
    }
  };

  const handleOpenTable = async () => {
    try {
      setIsUpdating(true);
      setError(null);
      await openTable(table.id);
      onTableStatusChanged();
      onClose();
    } catch (err) {
      console.error('Failed to open table:', err);
      setError(err instanceof Error ? err.message : 'Failed to open table');
    } finally {
      setIsUpdating(false);
    }
  };

  const handleReleaseTable = async () => {
    try {
      setIsUpdating(true);
      setError(null);
      await releaseTable(table.tableNumber);
      onTableStatusChanged();
      onClose();
    } catch (err) {
      console.error('Failed to release table:', err);
      setError(err instanceof Error ? err.message : 'Failed to release table');
    } finally {
      setIsUpdating(false);
    }
  };

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div className={styles.header}>
          <div className={styles.tableInfo}>
            <span className={styles.tableNumber}>
              {t('server.table', 'Table')} {table.tableNumber}
            </span>
            <span className={styles.statusBadge}>
              {getStatusIcon()} {getStatusLabel()}
            </span>
          </div>
          <button className={styles.closeButton} onClick={onClose}>
            ✕
          </button>
        </div>

        <div className={styles.content}>
          {error && <div className={styles.errorBanner}>{error}</div>}

          {/* Table Actions */}
          <div className={styles.section}>
            <h3 className={styles.sectionTitle}>{t('server.actions', 'Actions')}</h3>
            <div className={styles.actionButtons}>
              {table.status !== 'closed' && (
                <button className={styles.takeOrderButton} onClick={onTakeOrder}>
                  🍽️ {t('server.take_order', 'Take Order')}
                </button>
              )}

              {/* Mark as Available button for reserved tables */}
              {table.status === 'reserved' && (
                <button className={styles.releaseTableButton} onClick={handleReleaseTable} disabled={isUpdating}>
                  {isUpdating ? '...' : '✅'} {t('server.mark_available', 'Mark as Available')}
                </button>
              )}

              {/* Mark as Available button for occupied tables with no active orders in UI */}
              {table.status === 'occupied' && activeOrders.length === 0 && (
                <button
                  className={styles.releaseTableButton}
                  onClick={async () => {
                    try {
                      setIsUpdating(true);
                      setError(null);
                      // Complete any lingering orders in the database for this table
                      const result = await completeAllTableOrders(table.tableNumber);

                      if (result.totalProcessed > 0) {
                      }

                      onTableStatusChanged();
                      onClose();
                    } catch (err) {
                      console.error('Failed to free table:', err);
                      setError(err instanceof Error ? err.message : 'Failed to free table');
                      setIsUpdating(false);
                    }
                  }}
                  disabled={isUpdating}
                >
                  {isUpdating ? '...' : '✅'} {t('server.mark_available', 'Mark as Available')}
                </button>
              )}

              {/* Info for occupied tables with orders - status clears when orders complete */}
              {table.status === 'occupied' && activeOrders.length > 0 && (
                <p className={styles.actionHint}>
                  ℹ️ {t('server.occupied_hint', 'Table will become available when all orders are completed')}
                </p>
              )}

              {table.status === 'closed' ? (
                <button className={styles.openTableButton} onClick={handleOpenTable} disabled={isUpdating}>
                  {isUpdating ? '...' : '🔓'} {t('server.open_table', 'Open Table')}
                </button>
              ) : (
                <button
                  className={styles.closeTableButton}
                  onClick={handleCloseTable}
                  disabled={isUpdating || activeOrders.length > 0}
                  title={activeOrders.length > 0 ? 'Complete all orders first' : ''}
                >
                  {isUpdating ? '...' : '🔒'} {t('server.close_table', 'Close Table')}
                </button>
              )}
            </div>
            {activeOrders.length > 0 && table.status !== 'closed' && (
              <p className={styles.actionHint}>
                ⚠️ {t('server.close_hint', 'Complete all orders before closing the table')}
              </p>
            )}
          </div>

          {/* Table Info */}
          <div className={styles.section}>
            <h3 className={styles.sectionTitle}>{t('server.table_info', 'Table Information')}</h3>
            <div className={styles.infoGrid}>
              <div className={styles.infoItem}>
                <span className={styles.infoLabel}>{t('server.capacity', 'Capacity')}</span>
                <span className={styles.infoValue}>
                  👥 {table.maxGuests} {t('server.guests', 'guests')}
                </span>
              </div>
              <div className={styles.infoItem}>
                <span className={styles.infoLabel}>{t('server.location', 'Location')}</span>
                <span className={styles.infoValue}>{table.isOutdoor ? '🌳 Outdoor' : '🏠 Indoor'}</span>
              </div>
              {table.notes && (
                <div className={styles.infoItem}>
                  <span className={styles.infoLabel}>{t('server.notes', 'Notes')}</span>
                  <span className={styles.infoValue}>{table.notes}</span>
                </div>
              )}
            </div>
          </div>

          {/* Upcoming Reservation */}
          {table.upcomingReservation && (
            <div className={styles.section}>
              <h3 className={styles.sectionTitle}>🗓️ {t('server.upcoming_reservation', 'Upcoming Reservation')}</h3>
              <div className={styles.reservationCard}>
                <div className={styles.reservationTime}>
                  {table.upcomingReservation.startTime.substring(0, 5)} -{' '}
                  {table.upcomingReservation.endTime.substring(0, 5)}
                </div>
                <div className={styles.reservationDetails}>
                  <span>👤 {table.upcomingReservation.customerName}</span>
                  <span>
                    👥 {table.upcomingReservation.numberOfGuests} {t('server.guests', 'guests')}
                  </span>
                </div>
                {table.upcomingReservation.specialRequests && (
                  <div className={styles.reservationNotes}>📝 {table.upcomingReservation.specialRequests}</div>
                )}
              </div>
            </div>
          )}

          {/* Active Orders */}
          <div className={styles.section}>
            <h3 className={styles.sectionTitle}>
              📋 {t('server.active_orders', 'Active Orders')} ({activeOrders.length})
            </h3>
            {activeOrders.length === 0 ? (
              <div className={styles.emptyOrders}>{t('server.no_orders_table', 'No active orders for this table')}</div>
            ) : (
              <div className={styles.ordersList}>
                {activeOrders.map((order) => (
                  <div key={order.id} className={styles.orderItem}>
                    <div className={styles.orderHeader}>
                      <span className={styles.orderNumber}>#{order.orderNumber}</span>
                      <span className={styles.orderStatus}>{order.status}</span>
                    </div>
                    <div className={styles.orderItems}>
                      {order.items.map((item, idx) => (
                        <div key={idx} className={styles.orderItemLine}>
                          <span>
                            {item.quantity}× {item.productName}
                          </span>
                        </div>
                      ))}
                    </div>
                    <div className={styles.orderFooter}>
                      <span className={styles.orderTotal}>CHF {order.total.toFixed(2)}</span>
                      {order.status === 'Ready' && (
                        <button
                          className={styles.completeButton}
                          onClick={() => onUpdateOrderStatus(order.id, 'Completed')}
                        >
                          {t('server.complete_order', 'Complete Order')}
                        </button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
