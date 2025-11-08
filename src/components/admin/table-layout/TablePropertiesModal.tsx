import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useTableHelpers } from '@/hooks/useTableHelpers';
import { X } from 'lucide-react';
import type { TableDto } from '@/types/reservation';
import styles from './TablePropertiesModal.module.css';

interface TablePropertiesModalProps {
  isOpen: boolean;
  table: TableDto;
  onClose: () => void;
  onUpdateTable: (updates: Partial<TableDto>) => void;
  onSave: (tableId: string, updates: Partial<TableDto>) => Promise<void>;
}

export default function TablePropertiesModal({
  isOpen,
  table,
  onClose,
  onUpdateTable,
  onSave,
}: TablePropertiesModalProps) {
  const { t } = useTranslation();
  const { allShapes, getShapeLabel } = useTableHelpers();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!isOpen) return null;

  const handleSave = async () => {
    try {
      setSaving(true);
      setError(null);

      // Extract only the fields that can be updated
      const updates: Partial<TableDto> = {
        tableNumber: table.tableNumber,
        maxGuests: table.maxGuests,
        shape: table.shape,
        isActive: table.isActive,
        isOutdoor: table.isOutdoor,
        notes: table.notes,
      };

      await onSave(table.id, updates);
      onClose();
    } catch (err: any) {
      setError(err.message || t('failed_save_changes', 'Failed to save changes'));
    } finally {
      setSaving(false);
    }
  };

  const handleOverlayClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget && !saving) {
      onClose();
    }
  };

  return (
    <div className={styles.overlay} onClick={handleOverlayClick}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <button
          onClick={onClose}
          className={styles.closeButton}
          aria-label={t('close', 'Close')}
          disabled={saving}
        >
          <X size={20} />
        </button>

        <h2>{t('table_properties', 'Table Properties')}</h2>

        {error && <div className={styles.error}>{error}</div>}

        <div className={styles.formGroup}>
            <label>{t('table_number', 'Table Number')}</label>
            <input
              type="text"
              value={table.tableNumber}
              onChange={(e) => onUpdateTable({ tableNumber: e.target.value })}
              className={styles.input}
              placeholder={t('table_number_placeholder', 'e.g., 1, 2, 3')}
              disabled={saving}
            />
          </div>

          <div className={styles.formGroup}>
            <label>{t('max_guests', 'Max Guests')}</label>
            <input
              type="number"
              value={table.maxGuests}
              onChange={(e) => onUpdateTable({ maxGuests: parseInt(e.target.value) || 1 })}
              className={styles.input}
              min="1"
              max="20"
              disabled={saving}
            />
          </div>

          <div className={styles.formGroup}>
            <label>{t('shape', 'Shape')}</label>
            <select
              value={table.shape || 'circle'}
              onChange={(e) => onUpdateTable({ shape: e.target.value })}
              className={styles.select}
              disabled={saving}
            >
              {allShapes.map((shape) => (
                <option key={shape} value={shape}>
                  {getShapeLabel(shape)}
                </option>
              ))}
            </select>
          </div>

          <div className={styles.formGroup}>
            <label>{t('status', 'Status')}</label>
            <div className={styles.chipContainer}>
              <button
                type="button"
                onClick={() => onUpdateTable({ isActive: !table.isActive })}
                className={`${styles.chip} ${table.isActive ? styles.chipActive : styles.chipInactive}`}
                disabled={saving}
              >
                {table.isActive ? `✓ ${t('active', 'Active')}` : `○ ${t('inactive', 'Inactive')}`}
              </button>
            </div>
          </div>

          <div className={styles.formGroup}>
            <label>{t('location', 'Location')}</label>
            <div className={styles.chipContainer}>
              <button
                type="button"
                onClick={() => onUpdateTable({ isOutdoor: false })}
                className={`${styles.chip} ${!table.isOutdoor ? styles.chipSelected : ''}`}
                disabled={saving}
              >
                🏠 {t('indoor', 'Indoor')}
              </button>
              <button
                type="button"
                onClick={() => onUpdateTable({ isOutdoor: true })}
                className={`${styles.chip} ${table.isOutdoor ? styles.chipSelected : ''}`}
                disabled={saving}
              >
                🌳 {t('outdoor', 'Outdoor')}
              </button>
            </div>
          </div>

          <div className={styles.formGroup}>
            <label>{t('notes_visible_customers', 'Notes (visible to customers)')}</label>
            <textarea
              value={table.notes || ''}
              onChange={(e) => onUpdateTable({ notes: e.target.value })}
              className={styles.textarea}
              placeholder={t('notes_placeholder', 'e.g., Near window, Quiet corner, etc.')}
              rows={3}
              maxLength={500}
              disabled={saving}
            />
            <small className={styles.charCount}>
              {t('characters_count', '{{count}}/500 characters', { count: (table.notes || '').length })}
            </small>
          </div>

          <div className={styles.infoGroup}>
            <div className={styles.infoItem}>
              <span className={styles.infoLabel}>{t('position_x', 'Position X')}:</span>
              <span className={styles.infoValue}>
                {table.positionX.toFixed(1)}px
              </span>
            </div>
            <div className={styles.infoItem}>
              <span className={styles.infoLabel}>{t('position_y', 'Position Y')}:</span>
              <span className={styles.infoValue}>
                {table.positionY.toFixed(1)}px
              </span>
            </div>
          </div>

          <div className={styles.buttonGroup}>
            <button
              type="button"
              onClick={onClose}
              className={styles.cancelButton}
              disabled={saving}
            >
              {t('cancel', 'Cancel')}
            </button>
            <button
              type="button"
              onClick={handleSave}
              className={styles.saveButton}
              disabled={saving}
            >
              {saving ? t('saving', 'Saving...') : t('save_changes', 'Save Changes')}
            </button>
          </div>
      </div>
    </div>
  );
}
