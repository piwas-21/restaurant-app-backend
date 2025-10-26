import React, { useState } from 'react';
import { OrderStatus } from '@/types/order';
import { X, Loader2 } from 'lucide-react';
import styles from './BulkStatusUpdateModal.module.css';

interface BulkStatusUpdateModalProps {
  selectedCount: number;
  onClose: () => void;
  onConfirm: (status: OrderStatus, notes: string) => Promise<void>;
  progress?: { current: number; total: number };
  isUpdating: boolean;
}

export const BulkStatusUpdateModal: React.FC<BulkStatusUpdateModalProps> = ({
  selectedCount,
  onClose,
  onConfirm,
  progress,
  isUpdating,
}) => {
  const [status, setStatus] = useState<OrderStatus>('Pending');
  const [notes, setNotes] = useState('');

  const handleConfirm = async () => {
    if (!notes.trim()) {
      return;
    }
    await onConfirm(status, notes);
  };

  return (
    <div className={styles.modal} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2>Update Status for {selectedCount} Orders</h2>
          <button className={styles.closeButton} onClick={onClose} aria-label="Close">
            <X size={24} />
          </button>
        </div>

        <div className={styles.modalBody}>
          <div className={styles.formGroup}>
            <label htmlFor="bulkStatus">New Status</label>
            <select
              id="bulkStatus"
              value={status}
              onChange={(e) => setStatus(e.target.value as OrderStatus)}
              className={styles.select}
            >
              <option value="Pending">Pending</option>
              <option value="Confirmed">Confirmed</option>
              <option value="Preparing">Preparing</option>
              <option value="Ready">Ready</option>
              <option value="InTransit">In Transit</option>
              <option value="Completed">Completed</option>
              <option value="Cancelled">Cancelled</option>
            </select>
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="bulkStatusNotes">Notes (Required)</label>
            <textarea
              id="bulkStatusNotes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Enter reason for status update..."
              rows={4}
              className={styles.textarea}
              required
            />
          </div>

          {isUpdating && progress && (
            <div className={styles.progressBar}>
              <div className={styles.progressText}>
                Updating {progress.current} of {progress.total}...
              </div>
              <div className={styles.progressTrack}>
                <div
                  className={styles.progressFill}
                  style={{
                    width: `${(progress.current / progress.total) * 100}%`,
                  }}
                />
              </div>
            </div>
          )}
        </div>

        <div className={styles.modalFooter}>
          <button onClick={onClose} className={styles.cancelButton} disabled={isUpdating}>
            Cancel
          </button>
          <button
            onClick={handleConfirm}
            className={styles.confirmButton}
            disabled={isUpdating || !notes.trim()}
          >
            {isUpdating ? (
              <>
                <Loader2 size={16} className={styles.spinner} />
                Updating...
              </>
            ) : (
              <>Update {selectedCount} Orders</>
            )}
          </button>
        </div>
      </div>
    </div>
  );
};
