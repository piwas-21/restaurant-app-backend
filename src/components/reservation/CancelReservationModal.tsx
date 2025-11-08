"use client";

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from './CancelReservationModal.module.css';

interface CancelReservationModalProps {
  isOpen: boolean;
  onConfirm: () => void;
  onCancel: () => void;
  isLoading?: boolean;
}

export default function CancelReservationModal({
  isOpen,
  onConfirm,
  onCancel,
  isLoading = false,
}: CancelReservationModalProps) {
  const { t } = useTranslation();

  if (!isOpen) {
    return null;
  }

  return (
    <div className={styles.modalOverlay} onClick={onCancel}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2 className={styles.modalTitle}>
            {t('cancel_reservation_title', 'Cancel Reservation')}
          </h2>
        </div>

        <div className={styles.modalBody}>
          <p className={styles.confirmMessage}>
            {t('cancel_reservation_confirm_message', 'Are you sure you want to cancel this reservation?')}
          </p>
        </div>

        <div className={styles.modalActions}>
          <button
            className={styles.cancelButton}
            onClick={onCancel}
            disabled={isLoading}
          >
            {t('cancel', 'Cancel')}
          </button>
          <button
            className={styles.confirmButton}
            onClick={onConfirm}
            disabled={isLoading}
          >
            {isLoading
              ? t('my_reservations_cancelling', 'Cancelling...')
              : t('cancel_reservation', 'Cancel Reservation')}
          </button>
        </div>
      </div>
    </div>
  );
}
