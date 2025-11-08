"use client";

import React, { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import styles from './CancelSuccessModal.module.css';
import { CheckCircle } from 'lucide-react';

interface CancelSuccessModalProps {
  isOpen: boolean;
  onClose: () => void;
  autoCloseMs?: number;
}

export default function CancelSuccessModal({
  isOpen,
  onClose,
  autoCloseMs = 3000,
}: CancelSuccessModalProps) {
  const { t } = useTranslation();

  useEffect(() => {
    if (isOpen && autoCloseMs > 0) {
      const timer = setTimeout(onClose, autoCloseMs);
      return () => clearTimeout(timer);
    }
  }, [isOpen, autoCloseMs, onClose]);

  if (!isOpen) {
    return null;
  }

  return (
    <div className={styles.modalOverlay} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <div className={styles.successIcon}>
            <CheckCircle size={32} />
          </div>
        </div>

        <h2 className={styles.modalTitle}>
          {t('my_reservations_cancelled_success', 'Reservation cancelled successfully')}
        </h2>

        <div className={styles.modalBody}>
          <p className={styles.successMessage}>
            {t('reservation_cancel_success_message', 'Your reservation has been cancelled. You can make a new reservation anytime.')}
          </p>
        </div>

        <div className={styles.modalActions}>
          <button className={styles.closeButton} onClick={onClose}>
            {t('close', 'Close')}
          </button>
        </div>
      </div>
    </div>
  );
}
