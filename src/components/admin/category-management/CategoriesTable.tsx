'use client';

import React from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';
import { Category } from '@/app/admin/menu-management/interfaces';

interface CategoriesTableProps {
  categories: Category[];
  isLoading: boolean;
  error: string | null;
  onEdit: (category: Category) => void;
  onDelete: (category: Category) => void;
}

const CategoriesTable: React.FC<CategoriesTableProps> = ({ categories, isLoading, error, onEdit, onDelete }) => {
  const { t } = useTranslation();
  const router = useRouter();

  const handleViewProducts = (category: Category) => {
    router.push(`/admin/menu-management?categoryId=${category.id}&categoryName=${encodeURIComponent(category.name)}`);
  };

  if (isLoading) return <p>{t('loading_categories')}</p>;
  if (error) return <p className={styles.error}>{error}</p>;

  return (
    <div className={styles.adminTableContainer}>
      <table className={styles.adminTable}>
        <thead>
          <tr>
            <th>{t('category_name')}</th>
            <th>{t('is_active')}</th>
            <th>{t('display_order')}</th>
            <th>{t('product_count')}</th>
            <th>{t('actions_header')}</th>
          </tr>
        </thead>
        <tbody>
          {categories.length > 0 ? (
            categories.map((category) => (
              <tr key={category.id}>
                <td>{category.name}</td>
                <td>{category.isActive ? t('yes') : t('no')}</td>
                <td>{category.displayOrder}</td>
                <td>{category.productCount ?? 0}</td>
                <td className={styles.actionsCell}>
                  <button onClick={() => onEdit(category)} className={`${styles.adminButton} ${styles.edit}`}>
                    {t('edit')}
                  </button>
                  <button onClick={() => onDelete(category)} className={`${styles.adminButton} ${styles.delete}`}>
                    {t('delete')}
                  </button>
                  <button onClick={() => handleViewProducts(category)} className={`${styles.adminButton} ${styles.view}`}>
                    {t('view_products')}
                  </button>
                </td>
              </tr>
            ))
          ) : (
            <tr>
              <td colSpan={5}>{t('no_categories_found')}</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
};

export default CategoriesTable;
