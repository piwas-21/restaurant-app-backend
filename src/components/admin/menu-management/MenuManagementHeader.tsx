'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';
import { Category } from '@/app/admin/menu-management/interfaces';
import { useRouter } from 'next/navigation';

interface MenuManagementHeaderProps {
  pageTitle: string;
  categories: Category[];
  selectedCategoryId: string | null;
  onCategoryChange: (event: React.ChangeEvent<HTMLSelectElement>) => void;
  onOpenCreateModal: () => void;
}

const MenuManagementHeader: React.FC<MenuManagementHeaderProps> = ({
  pageTitle,
  categories,
  selectedCategoryId,
  onCategoryChange,
  onOpenCreateModal,
}) => {
  const { t } = useTranslation();
  const router = useRouter();

  return (
    <div className={styles.adminHeader}>
      <h1>{pageTitle}</h1>
      <div className={styles.adminHeaderControls}>
        <select onChange={onCategoryChange} value={selectedCategoryId || 'all'} className={styles.adminSelect}>
          <option value="all">{t('all_categories_nav')}</option>
          {categories.map((category) => (
            <option key={category.id} value={category.id}>{category.name}</option>
          ))}
        </select>
        <div className={styles.tooltipContainer}>
          <button
            className={`${styles.adminButton} ${styles.add}`}
            onClick={onOpenCreateModal}
            disabled={!selectedCategoryId}
          >
            {t('create_new_product')}
          </button>
          {!selectedCategoryId && (
            <span className={styles.tooltipText}>{t('select_category_to_create_product')}</span>
          )}
        </div>
        <button className={styles.adminButton} onClick={() => router.push('/admin/category-management')}>
          {t('back_to_categories')}
        </button>
      </div>
    </div>
  );
};

export default MenuManagementHeader;
