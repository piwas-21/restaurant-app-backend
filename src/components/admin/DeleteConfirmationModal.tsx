/**
 * Delete Confirmation Modal
 *
 * Confirmation dialog for deleting orders with safety checks
 */

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { OrderDto } from '@/types/order';
import { AlertTriangle } from 'lucide-react';
import styles from './DeleteConfirmationModal.module.css';

interface DeleteConfirmationModalProps {
  order: OrderDto;
  onClose: () => void;
  onConfirm: () => Promise<void>;
}

export default function DeleteConfirmationModal({
  order,
  onClose,
  onConfirm,
}: DeleteConfirmationModalProps) {
  const { t } = useTranslation();
  const [confirmText, setConfirmText] = useState('');
  const [isDeleting, setIsDeleting] = useState(false);

  const isConfirmValid = confirmText === order.orderNumber;

  const handleConfirm = async () => {
    if (!isConfirmValid) return;

    setIsDeleting(true);
    try {
      await onConfirm();
      onClose();
    } catch (error) {
      setIsDeleting(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && isConfirmValid && !isDeleting) {
      handleConfirm();
    } else if (e.key === 'Escape' && !isDeleting) {
      onClose();
    }
  };

  const handleOverlayClick = () => {
    if (!isDeleting) {
      onClose();
    }
  };

  return (
    <div className={styles.modalOverlay} onClick={handleOverlayClick}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.warningIcon}>
          <AlertTriangle size={48} />
        </div>

        <h2>{t('delete_order_confirm', 'Delete Order?')}</h2>

        <div className={styles.orderInfo}>
          <div className={styles.infoRow}>
            <span className={styles.label}>{t('order_number', 'Order Number')}:</span>
            <span className={styles.value}>{order.orderNumber}</span>
          </div>
          <div className={styles.infoRow}>
            <span className={styles.label}>{t('customer_name', 'Customer')}:</span>
            <span className={styles.value}>{order.customerName || 'N/A'}</span>
          </div>
          <div className={styles.infoRow}>
            <span className={styles.label}>{t('total', 'Total')}:</span>
            <span className={styles.value}>CHF {order.total.toFixed(2)}</span>
          </div>
        </div>

        <p className={styles.warningText}>
          {t(
            'delete_order_warning',
            'This action cannot be undone. The order will be permanently deleted.'
          )}
        </p>

        <div className={styles.confirmSection}>
          <label htmlFor="confirm-input" className={styles.confirmLabel}>
            {t('confirm_delete_order', 'Type the order number to confirm deletion')}:
          </label>
          <input
            id="confirm-input"
            type="text"
            value={confirmText}
            onChange={(e) => setConfirmText(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={order.orderNumber}
            className={styles.confirmInput}
            autoFocus
            disabled={isDeleting}
          />
        </div>

        <div className={styles.buttonGroup}>
          <button
            type="button"
            onClick={onClose}
            className={styles.cancelButton}
            disabled={isDeleting}
          >
            {t('cancel', 'Cancel')}
          </button>
          <button
            type="button"
            onClick={handleConfirm}
            className={styles.deleteButton}
            disabled={!isConfirmValid || isDeleting}
          >
            {isDeleting ? t('deleting', 'Deleting...') : t('delete_order', 'Delete Order')}
          </button>
        </div>
      </div>
    </div>
  );
}
