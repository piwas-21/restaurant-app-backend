import React, { useState } from 'react';
import { OrderDto } from '@/types/order';
import { useTranslation } from 'react-i18next';
import { X, Star, Loader2 } from 'lucide-react';
import styles from './FocusOrderModal.module.css';

interface FocusOrderModalProps {
  order: OrderDto;
  onClose: () => void;
  onConfirm: (isFocusOrder: boolean, priority?: number, reason?: string) => Promise<void>;
}

export const FocusOrderModal: React.FC<FocusOrderModalProps> = ({
  order,
  onClose,
  onConfirm,
}) => {
  const { t } = useTranslation();
  const [priority, setPriority] = useState(1);
  const [reason, setReason] = useState('');
  const [isProcessing, setIsProcessing] = useState(false);

  const handleConfirm = async () => {
    setIsProcessing(true);
    try {
      await onConfirm(
        !order.isFocusOrder,
        order.isFocusOrder ? undefined : priority,
        order.isFocusOrder ? undefined : reason || undefined
      );
    } finally {
      setIsProcessing(false);
    }
  };

  return (
    <div className={styles.modal} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2>
            {order.isFocusOrder
              ? t('remove_focus_order', 'Remove Focus Order')
              : t('mark_as_focus_order', 'Mark as Focus Order')}
          </h2>
          <button onClick={onClose} className={styles.closeButton}>
            <X size={20} />
          </button>
        </div>
        <div className={styles.modalBody}>
          <p className={styles.orderInfo}>
            {t('order', 'Order')} #{order.orderNumber}
          </p>
          {!order.isFocusOrder && (
            <>
              <div className={styles.formGroup}>
                <label htmlFor="priority">{t('priority', 'Priority')}:</label>
                <input
                  type="number"
                  id="priority"
                  value={priority}
                  onChange={(e) => setPriority(parseInt(e.target.value, 10))}
                  min="1"
                  max="10"
                  className={styles.formInput}
                />
                <small>{t('priority_hint', '1 = Highest priority, 10 = Lowest priority')}</small>
              </div>
              <div className={styles.formGroup}>
                <label htmlFor="focusReason">{t('reason_optional', 'Reason (Optional)')}:</label>
                <textarea
                  id="focusReason"
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  placeholder={t('focus_reason_placeholder', 'Why is this order a priority?')}
                  className={styles.formTextarea}
                  rows={3}
                />
              </div>
            </>
          )}
          {order.isFocusOrder && (
            <p className={styles.confirmMessage}>
              {t('remove_focus_confirm', 'Are you sure you want to remove this order from focus orders?')}
            </p>
          )}
        </div>
        <div className={styles.modalFooter}>
          <button onClick={onClose} className={styles.cancelButton} disabled={isProcessing}>
            {t('cancel', 'Cancel')}
          </button>
          <button
            onClick={handleConfirm}
            className={order.isFocusOrder ? styles.removeButton : styles.confirmButton}
            disabled={isProcessing}
          >
            {isProcessing ? (
              <>
                <Loader2 size={18} className={styles.spinner} />
                {t('processing', 'Processing...')}
              </>
            ) : order.isFocusOrder ? (
              <>
                <X size={18} />
                {t('remove_focus', 'Remove Focus')}
              </>
            ) : (
              <>
                <Star size={18} />
                {t('mark_as_focus', 'Mark as Focus')}
              </>
            )}
          </button>
        </div>
      </div>
    </div>
  );
};
