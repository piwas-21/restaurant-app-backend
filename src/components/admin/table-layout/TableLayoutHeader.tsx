import React from 'react';
import styles from './TableLayoutHeader.module.css';

interface CanvasSizeOption {
  width: number;
  height: number;
  label: string;
}

interface TableLayoutHeaderProps {
  canvasSize: string;
  canvasSizes: Record<string, CanvasSizeOption>;
  selectionMode: boolean;
  selectedCount: number;
  saving: boolean;
  onCanvasSizeChange: (size: string) => void;
  onCreateTable: () => void;
  onToggleSelectionMode: () => void;
  onSaveLayout: () => void;
  onBulkActivate?: () => void;
  onBulkDeactivate?: () => void;
  onBulkDelete?: () => void;
  onCancelSelection?: () => void;
}

export default function TableLayoutHeader({
  canvasSize,
  canvasSizes,
  selectionMode,
  selectedCount,
  saving,
  onCanvasSizeChange,
  onCreateTable,
  onToggleSelectionMode,
  onSaveLayout,
  onBulkActivate,
  onBulkDeactivate,
  onBulkDelete,
  onCancelSelection,
}: TableLayoutHeaderProps) {
  return (
    <div className={styles.header}>
      <div>
        <h1 className={styles.title}>Table Layout Editor</h1>
        <p className={styles.subtitle}>Drag tables to reposition them on the floor plan</p>
      </div>
      <div className={styles.headerActions}>
        {!selectionMode ? (
          <>
            <div className={styles.canvasSizeSelector}>
              <label>Canvas Size:</label>
              <select
                value={canvasSize}
                onChange={(e) => onCanvasSizeChange(e.target.value)}
                className={styles.sizeSelect}
              >
                {Object.entries(canvasSizes).map(([key, { label }]) => (
                  <option key={key} value={key}>{label}</option>
                ))}
              </select>
            </div>
            <button
              onClick={onCreateTable}
              className={styles.createButton}
            >
              + Create Table
            </button>
            <button
              onClick={onToggleSelectionMode}
              className={styles.selectionModeButton}
            >
              Select Multiple
            </button>
            <button
              onClick={onSaveLayout}
              disabled={saving}
              className={styles.saveButton}
            >
              {saving ? 'Saving...' : 'Save Layout'}
            </button>
          </>
        ) : (
          <>
            <div className={styles.selectionInfo}>
              {selectedCount} table(s) selected
            </div>
            <button
              onClick={onBulkActivate}
              className={styles.bulkActionButton}
            >
              Activate Selected
            </button>
            <button
              onClick={onBulkDeactivate}
              className={styles.bulkActionButton}
            >
              Deactivate Selected
            </button>
            <button
              onClick={onBulkDelete}
              className={`${styles.bulkActionButton} ${styles.deleteAction}`}
            >
              Delete Selected
            </button>
            <button
              onClick={onCancelSelection}
              className={styles.cancelButton}
            >
              Cancel
            </button>
          </>
        )}
      </div>
    </div>
  );
}
