'use client';

import React from 'react';
import Link from 'next/link';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';
import { Product } from '@/app/admin/menu-management/interfaces';

interface ProductsTableProps {
  products: Product[];
  isLoading: boolean;
  error: string | null;
  onEdit: (productId: string) => void;
}

const ProductsTable: React.FC<ProductsTableProps> = ({ products, isLoading, error, onEdit }) => {
  const { t } = useTranslation();

  if (isLoading) return <p>{t('loading_products')}</p>;
  if (error) return <p className={styles.error}>{error}</p>;

  return (
    <div className={styles.adminTableContainer}>
      <table className={styles.adminTable}>
        <thead>
          <tr>
            <th>{t('product_name')}</th>
            <th>{t('base_price')}</th>
            <th>{t('active')}</th>
            <th>{t('available')}</th>
            <th>{t('actions_header')}</th>
          </tr>
        </thead>
        <tbody>
          {products.length > 0 ? (
            products.map((product) => (
              <tr key={product.id}>
                <td>{product.name}</td>
                <td>${product.basePrice.toFixed(2)}</td>
                <td>{product.isActive ? t('yes') : t('no')}</td>
                <td>{product.isAvailable ? t('yes') : t('no')}</td>
                <td className={styles.actionsCell}>
                  <button onClick={() => onEdit(product.id)} className={`${styles.adminButton} ${styles.edit}`}>
                    {t('edit')}
                  </button>
                  <Link href={`/admin/menu-management/${product.id}`} className={`${styles.adminButton} ${styles.details}`}>
                    {t('details')}
                  </Link>
                </td>
              </tr>
            ))
          ) : (
            <tr>
              <td colSpan={5}>{t('no_products_found')}</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
};

export default ProductsTable;
