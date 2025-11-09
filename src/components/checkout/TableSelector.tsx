"use client";

import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Utensils, Loader2, Users, AlertCircle } from 'lucide-react';
import styles from './TableSelector.module.css';

interface Table {
  id: string;
  tableNumber: string;
  maxGuests: number;
  isOutdoor: boolean;
  isActive: boolean;
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

  const fetchAvailableTables = async () => {
    try {
      setLoading(true);
      setError(null);

      const response = await fetch(
        `${process.env.NEXT_PUBLIC_API_URL}/api/Tables?isActive=true`
      );

      if (!response.ok) {
        throw new Error('Failed to fetch tables');
      }

      const result = await response.json();

      if (result.success && result.data) {
        setTables(result.data);
      } else {
        setError(result.message || 'Failed to load tables');
      }
    } catch (err) {
      if (process.env.NODE_ENV === 'development') {
        // eslint-disable-next-line no-console
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
    <div className={styles.container}>
      <div className={styles.tableGrid}>
        {tables.map((table) => (
          <button
            key={table.id}
            onClick={() => !disabled && onTableSelect(table.tableNumber)}
            disabled={disabled}
            className={`${styles.tableCard} ${
              selectedTable === table.tableNumber ? styles.selected : ''
            } ${disabled ? styles.disabled : ''}`}
          >
            <div className={styles.tableIcon}>
              <Utensils size={28} />
            </div>
            <div className={styles.tableInfo}>
              <span className={styles.tableNumber}>{table.tableNumber}</span>
              <div className={styles.tableDetails}>
                <Users size={14} />
                <span className={styles.maxGuests}>
                  {t('max_guests_count', '{{count}} guests', { count: table.maxGuests })}
                </span>
              </div>
              {table.isOutdoor && (
                <span className={styles.outdoorBadge}>
                  {t('outdoor', 'Outdoor')}
                </span>
              )}
            </div>
            {selectedTable === table.tableNumber && (
              <div className={styles.selectedBadge}>✓</div>
            )}
          </button>
        ))}
      </div>
    </div>
  );
}
