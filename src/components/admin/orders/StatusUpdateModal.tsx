import React, { useState } from 'react';
import { OrderDto, OrderStatus } from '@/types/order';
import { useTranslation } from 'react-i18next';
import { X, CheckCircle, Loader2 } from 'lucide-react';
import { useOrderHelpers } from '@/hooks/useOrderHelpers';
import styles from './StatusUpdateModal.module.css';

interface StatusUpdateModalProps {
  order: OrderDto;
  onClose: () => void;
  onConfirm: (status: OrderStatus, notes: string) => Promise<void>;
}

export const StatusUpdateModal: React.FC<StatusUpdateModalProps> = ({
  order,
  onClose,
  onConfirm,
}) => {
  const { t } = useTranslation();
  const { getStatusLabel, statusOptions } = useOrderHelpers();
  const [newStatus, setNewStatus] = useState<OrderStatus>(order.status as OrderStatus);
  const [notes, setNotes] = useState('');
  const [isUpdating, setIsUpdating] = useState(false);

  const handleConfirm = async () => {
    setIsUpdating(true);
    try {
      await onConfirm(newStatus, notes);
    } finally {
      setIsUpdating(false);
    }
  };

  return (
    <div className={styles.modal} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2>{t('update_order_status', 'Update Order Status')}</h2>
          <button onClick={onClose} className={styles.closeButton}>
            <X size={20} />
          </button>
        </div>
        <div className={styles.modalBody}>
          <p className={styles.orderInfo}>
            {t('order', 'Order')} #{order.orderNumber}
          </p>
          <div className={styles.formGroup}>
            <label htmlFor="status">{t('new_status', 'New Status')}:</label>
            <select
              id="status"
              value={newStatus}
              onChange={(e) => setNewStatus(e.target.value as OrderStatus)}
              className={styles.formSelect}
            >
              {statusOptions.map((status) => (
                <option key={status} value={status}>
                  {getStatusLabel(status)}
                </option>
              ))}
            </select>
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="notes">{t('notes_optional', 'Notes (Optional)')}:</label>
            <textarea
              id="notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder={t('status_notes_placeholder', 'Add any notes about this status change...')}
              className={styles.formTextarea}
              rows={3}
            />
          </div>
        </div>
        <div className={styles.modalFooter}>
          <button onClick={onClose} className={styles.cancelButton} disabled={isUpdating}>
            {t('cancel', 'Cancel')}
          </button>
          <button onClick={handleConfirm} className={styles.confirmButton} disabled={isUpdating}>
            {isUpdating ? (
              <>
                <Loader2 size={18} className={styles.spinner} />
                {t('updating', 'Updating...')}
              </>
            ) : (
              <>
                <CheckCircle size={18} />
                {t('update_status', 'Update Status')}
              </>
            )}
          </button>
        </div>
      </div>
    </div>
  );
};
