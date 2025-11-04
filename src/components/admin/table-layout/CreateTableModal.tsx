import React, { useState } from 'react';
import { CreateTableDto, TableDto } from '@/types/reservation';
import styles from './CreateTableModal.module.css';

interface CreateTableModalProps {
  isOpen: boolean;
  onClose: () => void;
  onCreateTable: (tableData: CreateTableDto) => Promise<TableDto>;
  existingTableNumbers: string[];
  canvasWidth: number;
  canvasHeight: number;
}

export const CreateTableModal: React.FC<CreateTableModalProps> = ({
  isOpen,
  onClose,
  onCreateTable,
  existingTableNumbers,
  canvasWidth,
  canvasHeight,
}) => {
  const [formData, setFormData] = useState({
    tableNumber: '',
    maxGuests: 4,
    shape: 'circle',
    isOutdoor: false,
    isActive: true,
    notes: '',
  });
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!isOpen) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!formData.tableNumber.trim()) {
      setError('Please enter a table number');
      return;
    }

    // Check if table number already exists
    if (existingTableNumbers.includes(formData.tableNumber)) {
      setError(`Table ${formData.tableNumber} already exists`);
      return;
    }

    try {
      setCreating(true);

      // Place new table in center of canvas
      const newTableData: CreateTableDto = {
        tableNumber: formData.tableNumber,
        maxGuests: formData.maxGuests,
        isActive: formData.isActive,
        isOutdoor: formData.isOutdoor,
        positionX: canvasWidth / 2 - 40, // Center horizontally
        positionY: canvasHeight / 2 - 40, // Center vertically
        width: formData.shape === 'rectangle' ? 100 : formData.shape === 'square' ? 60 : 80,
        height: formData.shape === 'rectangle' ? 70 : formData.shape === 'square' ? 60 : 80,
        shape: formData.shape,
        notes: formData.notes || undefined,
      };

      await onCreateTable(newTableData);

      // Reset form
      setFormData({
        tableNumber: '',
        maxGuests: 4,
        shape: 'circle',
        isOutdoor: false,
        isActive: true,
        notes: '',
      });

      onClose();
    } catch (error: any) {
      setError(error.message || 'Failed to create table');
    } finally {
      setCreating(false);
    }
  };

  const handleOverlayClick = () => {
    if (!creating) {
      onClose();
    }
  };

  return (
    <div className={styles.modalOverlay} onClick={handleOverlayClick}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <h2>Create New Table</h2>

        {error && <div className={styles.error}>{error}</div>}

        <form onSubmit={handleSubmit}>
          <div className={styles.formGroup}>
            <label htmlFor="tableNumber">Table Number *</label>
            <input
              id="tableNumber"
              type="text"
              value={formData.tableNumber}
              onChange={(e) => setFormData(prev => ({ ...prev, tableNumber: e.target.value }))}
              placeholder="e.g., T1, A1, etc."
              required
              disabled={creating}
            />
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="maxGuests">Max Guests</label>
            <input
              id="maxGuests"
              type="number"
              min="1"
              max="20"
              value={formData.maxGuests}
              onChange={(e) => setFormData(prev => ({ ...prev, maxGuests: parseInt(e.target.value) || 1 }))}
              disabled={creating}
            />
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="shape">Shape</label>
            <select
              id="shape"
              value={formData.shape}
              onChange={(e) => setFormData(prev => ({ ...prev, shape: e.target.value }))}
              disabled={creating}
            >
              <option value="circle">Circle</option>
              <option value="square">Square</option>
              <option value="rectangle">Rectangle</option>
            </select>
          </div>

          <div className={styles.formGroup}>
            <label>Location</label>
            <div className={styles.chipGroup}>
              <button
                type="button"
                className={`${styles.chip} ${formData.isOutdoor ? styles.chipActive : ''}`}
                onClick={() => setFormData(prev => ({ ...prev, isOutdoor: !prev.isOutdoor }))}
                disabled={creating}
              >
                <span className={styles.chipLabel}>Outdoor</span>
              </button>
            </div>
          </div>

          <div className={styles.formGroup}>
            <label>Status</label>
            <div className={styles.chipGroup}>
              <button
                type="button"
                className={`${styles.chip} ${formData.isActive ? styles.chipActive : ''}`}
                onClick={() => setFormData(prev => ({ ...prev, isActive: !prev.isActive }))}
                disabled={creating}
              >
                <span className={styles.chipLabel}>Active</span>
              </button>
            </div>
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="notes">Notes (optional)</label>
            <textarea
              id="notes"
              value={formData.notes}
              onChange={(e) => setFormData(prev => ({ ...prev, notes: e.target.value }))}
              placeholder="e.g., Near window, Quiet corner, etc."
              rows={3}
              maxLength={500}
              disabled={creating}
            />
            <small className={styles.charCounter}>
              {formData.notes.length}/500 characters
            </small>
          </div>

          <div className={styles.buttonGroup}>
            <button
              type="button"
              onClick={onClose}
              className={styles.cancelButton}
              disabled={creating}
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={creating}
              className={styles.submitButton}
            >
              {creating ? 'Creating...' : 'Create Table'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
