import React from 'react';
import type { TableDto } from '@/types/reservation';
import styles from './TablePropertiesSidebar.module.css';

interface TablePropertiesSidebarProps {
  selectedTable: TableDto | null;
  currentCanvasSize: { width: number; height: number };
  onUpdateTable: (updates: Partial<TableDto>) => void;
  onDeleteTable: () => void;
  onViewQRCode: () => void;
}

export default function TablePropertiesSidebar({
  selectedTable,
  currentCanvasSize,
  onUpdateTable,
  onDeleteTable,
  onViewQRCode,
}: TablePropertiesSidebarProps) {
  if (!selectedTable) {
    return (
      <div className={styles.sidebar}>
        <div className={styles.noSelection}>
          Click on a table to edit its properties
        </div>
      </div>
    );
  }

  return (
    <div className={styles.sidebar}>
      <div className={styles.sidebarHeader}>
        <h2>Table Properties</h2>
      </div>

      <div className={styles.formGroup}>
        <label>Table Number</label>
        <input
          type="text"
          value={selectedTable.tableNumber}
          onChange={(e) => onUpdateTable({ tableNumber: e.target.value })}
          className={styles.input}
          placeholder="e.g., 1, 2, 3"
        />
      </div>

      <div className={styles.formGroup}>
        <label>Max Guests</label>
        <input
          type="number"
          value={selectedTable.maxGuests}
          onChange={(e) => onUpdateTable({ maxGuests: parseInt(e.target.value) || 1 })}
          className={styles.input}
          min="1"
          max="20"
        />
      </div>

      <div className={styles.formGroup}>
        <label>Shape</label>
        <select
          value={selectedTable.shape || 'circle'}
          onChange={(e) => onUpdateTable({ shape: e.target.value })}
          className={styles.select}
        >
          <option value="circle">Circle</option>
          <option value="square">Square</option>
          <option value="rectangle">Rectangle</option>
        </select>
      </div>

      <div className={styles.formGroup}>
        <label>Status</label>
        <div className={styles.chipContainer}>
          <button
            type="button"
            onClick={() => onUpdateTable({ isActive: !selectedTable.isActive })}
            className={`${styles.chip} ${selectedTable.isActive ? styles.chipActive : styles.chipInactive}`}
          >
            {selectedTable.isActive ? '✓ Active' : '○ Inactive'}
          </button>
        </div>
      </div>

      <div className={styles.formGroup}>
        <label>Location</label>
        <div className={styles.chipContainer}>
          <button
            type="button"
            onClick={() => onUpdateTable({ isOutdoor: false })}
            className={`${styles.chip} ${!selectedTable.isOutdoor ? styles.chipSelected : ''}`}
          >
            🏠 Indoor
          </button>
          <button
            type="button"
            onClick={() => onUpdateTable({ isOutdoor: true })}
            className={`${styles.chip} ${selectedTable.isOutdoor ? styles.chipSelected : ''}`}
          >
            🌳 Outdoor
          </button>
        </div>
      </div>

      <div className={styles.formGroup}>
        <label>Notes (visible to customers)</label>
        <textarea
          value={selectedTable.notes || ''}
          onChange={(e) => onUpdateTable({ notes: e.target.value })}
          className={styles.textarea}
          placeholder="e.g., Near window, Quiet corner, etc."
          rows={3}
          maxLength={500}
        />
        <small className={styles.charCount}>
          {(selectedTable.notes || '').length}/500 characters
        </small>
      </div>

      <div className={styles.formGroup}>
        <label>Position X: {selectedTable.positionX.toFixed(1)}px ({((selectedTable.positionX / currentCanvasSize.width) * 100).toFixed(1)}%)</label>
      </div>

      <div className={styles.formGroup}>
        <label>Position Y: {selectedTable.positionY.toFixed(1)}px ({((selectedTable.positionY / currentCanvasSize.height) * 100).toFixed(1)}%)</label>
      </div>

      <div className={styles.formGroup} style={{ marginTop: '1.5rem' }}>
        <button
          onClick={onViewQRCode}
          className={styles.qrButton}
        >
          📱 View QR Code
        </button>
        <button
          onClick={onDeleteTable}
          className={styles.deleteButton}
        >
          Delete Table
        </button>
      </div>
    </div>
  );
}
