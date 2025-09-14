'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';

interface CategoryManagementHeaderProps {
  onOpenCreateModal: () => void;
}

const CategoryManagementHeader: React.FC<CategoryManagementHeaderProps> = ({ onOpenCreateModal }) => {
  const { t } = useTranslation();

  return (
    <div className={styles.adminHeader}>
      <h1>{t('admin_category_management_title')}</h1>
      <button className={`${styles.adminButton} ${styles.add}`} onClick={onOpenCreateModal}>
        {t('create_category')}
      </button>
    </div>
  );
};

export default CategoryManagementHeader;
