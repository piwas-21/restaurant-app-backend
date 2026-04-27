'use client';

import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Utensils, Loader2, Users, AlertCircle, Coffee, X } from 'lucide-react';
import styles from './TableSelector.module.css';

interface TableOccupant {
  customerName?: string;
  orderNumber?: string;
  orderDate: string;
  isLoggedInUser: boolean;
}

interface Table {
  id: string;
  tableNumber: string;
  maxGuests: number;
  isOutdoor: boolean;
  isActive: boolean;
  isReserved?: boolean;
  reservedUntil?: string;
  isOccupied?: boolean;
  activeOrderCount?: number;
  occupants?: TableOccupant[];
}

interface TableSelectorProps {
  selectedTable: string;
  onTableSelect: (tableNumber: string) => void;
  disabled?: boolean;
}

export default function TableSelector({ selectedTable, onTableSelect, disabled }: TableSelectorProps) {
  const { t } = useTranslation();
  const [tables, setTables] = useState<Table[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showShareModal, setShowShareModal] = useState(false);
  const [tableToShare, setTableToShare] = useState<Table | null>(null);

  const fetchAvailableTables = async () => {
    try {
      setLoading(true);
      setError(null);

      const response = await fetch(`${process.env.NEXT_PUBLIC_API_URL}/api/Tables?isActive=true`);

      if (!response.ok) {
        throw new Error('Failed to fetch tables');
      }

      const result = await response.json();

      if (result.success && result.data) {
        // Sort tables using natural/alphanumeric sorting
        const sortedTables = [...result.data].sort((a, b) => {
          return a.tableNumber.localeCompare(b.tableNumber, undefined, {
            numeric: true,
            sensitivity: 'base',
          });
        });
        setTables(sortedTables);
      } else {
        setError(result.message || 'Failed to load tables');
      }
    } catch (err) {
      if (process.env.NODE_ENV === 'development') {
        console.error('Error fetching tables:', err);
      }
      setError(t('error_loading_tables', 'Error loading tables. Please try again.'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchAvailableTables();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleTableClick = (table: Table) => {
    if (disabled || table.isReserved) return;

    // If table is occupied, show confirmation modal
    if (table.isOccupied && table.activeOrderCount && table.activeOrderCount > 0) {
      // Check if table is at max capacity (maxGuests + 1)
      const maxAllowed = table.maxGuests + 1;
      if (table.activeOrderCount >= maxAllowed) {
        // Table is full, don't allow more orders
        return;
      }
      setTableToShare(table);
      setShowShareModal(true);
    } else {
      onTableSelect(table.tableNumber);
    }
  };

  const confirmShareTable = () => {
    if (tableToShare) {
      onTableSelect(tableToShare.tableNumber);
      setShowShareModal(false);
      setTableToShare(null);
    }
  };

  const cancelShareTable = () => {
    setShowShareModal(false);
    setTableToShare(null);
  };

  const isTableFull = (table: Table) => {
    if (!table.isOccupied || !table.activeOrderCount) return false;
    const maxAllowed = table.maxGuests + 1;
    return table.activeOrderCount >= maxAllowed;
  };

  if (loading) {
    return (
      <div className={styles.loadingContainer}>
        <Loader2 className={styles.spinner} size={32} />
        <p>{t('loading_tables', 'Loading available tables...')}</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className={styles.errorContainer}>
        <AlertCircle size={24} className={styles.errorIcon} />
        <p className={styles.errorText}>{error}</p>
        <button onClick={fetchAvailableTables} className={styles.retryButton}>
          {t('retry', 'Retry')}
        </button>
      </div>
    );
  }

  if (tables.length === 0) {
    return (
      <div className={styles.emptyContainer}>
        <Utensils size={48} className={styles.emptyIcon} />
        <p>{t('no_tables_available', 'No tables are currently available')}</p>
      </div>
    );
  }

  return (
    <>
      <div className={styles.container}>
        <div className={styles.tableGrid}>
          {tables.map((table) => {
            const isFull = isTableFull(table);
            const isDisabled = disabled || table.isReserved || isFull;

            return (
              <button
                key={table.id}
                onClick={() => handleTableClick(table)}
                disabled={isDisabled}
                className={`${styles.tableCard} ${
                  selectedTable === table.tableNumber ? styles.selected : ''
                } ${isDisabled ? styles.disabled : ''} ${
                  table.isReserved ? styles.reserved : ''
                } ${table.isOccupied && !isFull ? styles.occupied : ''} ${isFull ? styles.full : ''}`}
              >
                <div className={styles.tableIcon}>
                  {table.isOccupied ? <Coffee size={28} /> : <Utensils size={28} />}
                </div>
                <div className={styles.tableInfo}>
                  <span className={styles.tableNumber}>{table.tableNumber}</span>
                  <div className={styles.tableDetails}>
                    <Users size={14} />
                    <span className={styles.maxGuests}>
                      {t('max_guests_count', '{{count}} guests', { count: table.maxGuests })}
                    </span>
                  </div>
                  {table.isOutdoor && <span className={styles.outdoorBadge}>{t('outdoor', 'Outdoor')}</span>}
                  {table.isReserved && <span className={styles.reservedBadge}>{t('reserved', 'Reserved')}</span>}
                  {table.isOccupied && !table.isReserved && (
                    <span className={isFull ? styles.fullBadge : styles.occupiedBadge}>
                      {isFull
                        ? t('table_full', 'Full')
                        : t('table_occupied_badge', '{{count}} order(s)', { count: table.activeOrderCount || 0 })}
                    </span>
                  )}
                </div>
                {selectedTable === table.tableNumber && <div className={styles.selectedBadge}>✓</div>}
              </button>
            );
          })}
        </div>
      </div>

      {/* Share Table Confirmation Modal */}
      {showShareModal && tableToShare && (
        <div className={styles.modalOverlay} onClick={cancelShareTable}>
          <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
            <button className={styles.modalClose} onClick={cancelShareTable}>
              <X size={20} />
            </button>
            <div className={styles.modalHeader}>
              <Coffee size={32} className={styles.modalIcon} />
              <h3>{t('share_table_title', 'Share Table?')}</h3>
            </div>
            <p className={styles.modalMessage}>
              {t(
                'share_table_message',
                'Table {{tableNumber}} is currently occupied. Would you like to join this table?',
                {
                  tableNumber: tableToShare.tableNumber,
                },
              )}
            </p>

            {tableToShare.occupants && tableToShare.occupants.length > 0 && (
              <div className={styles.occupantsList}>
                <p className={styles.occupantsTitle}>{t('share_table_occupants', 'Current orders at this table:')}</p>
                <ul>
                  {tableToShare.occupants.map((occupant, index) => (
                    <li key={index} className={styles.occupantItem}>
                      <span className={styles.occupantName}>{occupant.customerName || t('guest', 'Guest')}</span>
                      {occupant.orderNumber && <span className={styles.orderNumber}>#{occupant.orderNumber}</span>}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            <p className={styles.capacityInfo}>
              {t('remaining_capacity', '{{count}} more guests can join', {
                count: tableToShare.maxGuests + 1 - (tableToShare.activeOrderCount || 0),
              })}
            </p>

            <div className={styles.modalActions}>
              <button className={styles.cancelButton} onClick={cancelShareTable}>
                {t('share_table_cancel', 'Choose Another Table')}
              </button>
              <button className={styles.confirmButton} onClick={confirmShareTable}>
                {t('share_table_confirm', 'Yes, Join Table')}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
