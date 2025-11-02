'use client';

import React from 'react';
import { X, QrCode } from 'lucide-react';
import { useTableContext } from '@/contexts/TableContext';
import styles from './TableBanner.module.css';

interface TableBannerProps {
  position?: 'top' | 'floating';
}

export default function TableBanner({
  position = 'top'
}: TableBannerProps) {
  const { tableContext, clearTableContext, hasTableContext } = useTableContext();

  if (!hasTableContext) {
    return null;
  }

  const handleClear = () => {
    if (window.confirm('Clear table selection? You will need to scan the QR code again.')) {
      clearTableContext();
    }
  };

  return (
    <div
      className={`${styles.banner} ${position === 'floating' ? styles.floating : styles.top}`}
      role="status"
      aria-live="polite"
    >
      <div className={styles.content}>
        <div className={styles.icon}>
          <QrCode size={20} />
        </div>

        <div className={styles.info}>
          <span className={styles.label}>Ordering for Table</span>
          <span className={styles.tableNumber}>{tableContext.tableNumber}</span>
          {tableContext.isOutdoor && (
            <span className={styles.badge}>🌤️ Outdoor</span>
          )}
        </div>

        <button
          onClick={handleClear}
          className={styles.clearButton}
          aria-label="Clear table selection"
          title="Clear table selection"
        >
          <X size={18} />
        </button>
      </div>
    </div>
  );
}
