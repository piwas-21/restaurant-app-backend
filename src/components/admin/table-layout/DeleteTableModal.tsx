import React from 'react';
import { useTranslation } from 'react-i18next';
import { AlertTriangle } from 'lucide-react';
import styles from './DeleteTableModal.module.css';

interface DeleteTableModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: () => void;
  tableNumber?: string;
  tableCount?: number;
  isDeleting?: boolean;
}

export const DeleteTableModal: React.FC<DeleteTableModalProps> = ({
  isOpen,
  onClose,
  onConfirm,
  tableNumber,
  tableCount,
  isDeleting = false,
}) => {
  const { t } = useTranslation();

  if (!isOpen) return null;

  const isBulkDelete = tableCount !== undefined && tableCount > 0;

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

        <h2>
          {isBulkDelete ? t('delete_multiple_tables', 'Delete Multiple Tables?') : t('delete_table', 'Delete Table?')}
        </h2>

        <p className={styles.warningText}>
          {isBulkDelete ? (
            <>
              {t('delete_tables_confirmation', 'Are you sure you want to delete {{count}} table(s)?', { count: tableCount })}
              <br />
              {t('action_cannot_be_undone', 'This action cannot be undone.')}
            </>
          ) : (
            <>
              {t('delete_table_confirmation', 'Are you sure you want to delete Table {{tableNumber}}?', { tableNumber })}
              <br />
              {t('action_cannot_be_undone', 'This action cannot be undone.')}
            </>
          )}
        </p>

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
            onClick={onConfirm}
            disabled={isDeleting}
            className={styles.deleteButton}
          >
            {isDeleting ? t('deleting', 'Deleting...') : t('delete', 'Delete')}
          </button>
        </div>
      </div>
    </div>
  );
};
