'use client';

import React, { useState, useEffect, Suspense } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams, useRouter } from 'next/navigation';
import styles from '@/app/styles/AdminPage.module.css';
import { getProductsByCategoryId } from '@/services/menuService';
import CreateProductModal from '@/components/admin/CreateProductModal';

interface Product {
  id: string;
  name: string;
  basePrice: number;
  isActive: boolean;
  isAvailable: boolean;
}

const MenuManagementContent = () => {
  const { t } = useTranslation();
  const router = useRouter();
  const searchParams = useSearchParams();
  const categoryId = searchParams.get('categoryId');
  const categoryName = searchParams.get('categoryName');

  const [products, setProducts] = useState<Product[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isModalOpen, setIsModalOpen] = useState(false);

  const fetchProducts = async () => {
    if (categoryId) {
      setIsLoading(true);
      setError(null);
      try {
        const response = await getProductsByCategoryId(categoryId);
        if (response.success) {
          setProducts(response.data.items);
        } else {
          setError(response.message || 'Failed to fetch products');
        }
      } catch (err) {
        setError('An unexpected error occurred.');
      } finally {
        setIsLoading(false);
      }
    }
  };
  
  useEffect(() => {
    fetchProducts();
  }, [categoryId]);

  const handleProductCreated = () => {
    fetchProducts(); // Refetch products after a new one is created
  };
  
  const pageTitle = categoryName
    ? `${t('menu_items_for')} "${categoryName}"`
    : t('admin_menu_management_title');

  return (
    <div className={styles.adminContainer}>
      <div className={styles.adminHeader}>
        <h1>{pageTitle}</h1>
        <div>
          {categoryId && (
            <button className={`${styles.adminButton} ${styles.add}`} onClick={() => setIsModalOpen(true)}>
              {t('create_new_product')}
            </button>
          )}
          <button className={styles.adminButton} onClick={() => router.push('/admin/category-management')}>
            {t('back_to_categories')}
          </button>
        </div>
      </div>
      <div className={styles.adminContent}>
        {isLoading ? (
          <p>{t('loading_products')}</p>
        ) : error ? (
          <p className={styles.error}>{error}</p>
        ) : (
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
                      <td>{product.basePrice}</td>
                      <td>{product.isActive ? t('yes') : t('no')}</td>
                      <td>{product.isAvailable ? t('yes') : t('no')}</td>
                      <td>
                        {/* Edit and Delete buttons for products will go here */}
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
        )}
      </div>
      <CreateProductModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        onProductCreated={handleProductCreated}
        categoryId={categoryId}
      />
    </div>
  );
};

// Wrap the component in Suspense as useSearchParams requires it
const MenuManagementPage = () => (
  <Suspense fallback={<div>Loading...</div>}>
    <MenuManagementContent />
  </Suspense>
);

export default MenuManagementPage;
