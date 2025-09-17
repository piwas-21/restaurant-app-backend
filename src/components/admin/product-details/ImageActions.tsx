'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';
import detailsStyles from '@/app/styles/DetailsPage.module.css';

interface ImageActionsProps {
  isPrimary: boolean;
  sortOrder: number;
  onSetPrimary: () => void;
  onSortOrderChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  onSaveChanges: () => void;
  onDelete: () => void;
}

const ImageActions: React.FC<ImageActionsProps> = ({
  isPrimary,
  sortOrder,
  onSetPrimary,
  onSortOrderChange,
  onSaveChanges,
  onDelete,
}) => {
  const { t } = useTranslation();

  return (
    <div className={detailsStyles.imageActions}>
      <div className={detailsStyles.imageActionGroup}>
        <button
          onClick={onSetPrimary}
          disabled={isPrimary}
          className={`${styles.adminButton} ${isPrimary ? styles.disabled : ''}`}
        >
          {isPrimary ? t('primary') : t('set_as_primary')}
        </button>
        <div className={`${styles.formGroup} ${detailsStyles.imageActionGroup}`}>
          <label htmlFor="sortOrderInput">{t('sort_order')}</label>
          <input
            id="sortOrderInput"
            type="number"
            value={sortOrder}
            onChange={onSortOrderChange}
            className={detailsStyles.sortOrderInput}
          />
        </div>
      </div>
      <div className={detailsStyles.imageActionGroup}>
        <button onClick={onSaveChanges} className={`${styles.adminButton} ${styles.add}`}>
          {t('save_changes')}
        </button>
        <button onClick={onDelete} className={`${styles.adminButton} ${styles.delete}`}>
          {t('delete')}
        </button>
      </div>
    </div>
  );
};

export default ImageActions;
