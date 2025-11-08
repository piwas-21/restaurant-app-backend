'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from './DeleteDiscountModal.module.css';

interface DeleteDiscountModalProps {
  isOpen: boolean;
  discountName: string;
  onConfirm: () => void;
  onCancel: () => void;
  isLoading?: boolean;
}

export default function DeleteDiscountModal({
  isOpen,
  discountName,
  onConfirm,
  onCancel,
  isLoading = false,
}: DeleteDiscountModalProps) {
  const { t } = useTranslation();

  if (!isOpen) {
    return null;
  }

  return (
    <div className={styles.modalOverlay} onClick={onCancel}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2 className={styles.modalTitle}>
            {t('delete_discount_title', 'Delete Discount')}
          </h2>
        </div>

        <div className={styles.modalBody}>
          <p className={styles.confirmMessage}>
            {t('delete_discount_confirm_message', {
              name: discountName,
              defaultValue: `Are you sure you want to delete the discount "${discountName}"? This action cannot be undone.`
            })}
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
            className={styles.deleteButton}
            onClick={onConfirm}
            disabled={isLoading}
          >
            {isLoading
              ? t('deleting', 'Deleting...')
              : t('delete_discount_action', 'Delete Discount')}
          </button>
        </div>
      </div>
    </div>
  );
}
