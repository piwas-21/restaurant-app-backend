'use client';

import React, { useState, Suspense } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'next/navigation';
import { useMenuManagement } from '@/hooks/useMenuManagement';
import { getProductById } from '@/services/menuService';
import { deleteProduct } from '@/services/productService';
import styles from '@/app/styles/AdminPage.module.css';
import CreateProductModal from '@/components/admin/CreateProductModal';
import EditProductModal from '@/components/admin/EditProductModal';
import PageHeader from '@/components/admin/PageHeader';
import ProductsTable from '@/components/admin/menu-management/ProductsTable';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';

const MenuManagementContent = () => {
  const { t } = useTranslation();
  const searchParams = useSearchParams();
  const categoryName = searchParams.get('categoryName');

  const {
    products,
    categories,
    selectedCategoryId,
    isLoading,
    error,
    handleCategoryChange,
    fetchProducts,
  } = useMenuManagement();

  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [selectedProduct, setSelectedProduct] = useState<any | null>(null);
  const [isConfirmationOpen, setIsConfirmationOpen] = useState(false);
  const [productToDelete, setProductToDelete] = useState<string | null>(null);
  const [isResultModalOpen, setIsResultModalOpen] = useState(false);
  const [resultModalMessage, setResultModalMessage] = useState('');
  const [isResultModalSuccess, setIsResultModalSuccess] = useState(false);

  const handleOpenEditModal = async (productId: string) => {
    try {
      const response = await getProductById(productId);
      if (response.success) {
        setSelectedProduct(response.data);
        setIsEditModalOpen(true);
      } else {
        // Handle error
      }
    } catch (err) {
      // Handle error
    }
  };

  const handleDeleteClick = (productId: string) => {
    setProductToDelete(productId);
    setIsConfirmationOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (productToDelete) {
      const response = await deleteProduct(productToDelete);
      setIsConfirmationOpen(false);
      setResultModalMessage(response.data || response.message);
      setIsResultModalSuccess(response.success);
      setIsResultModalOpen(true);
      if (response.success) {
        fetchProducts();
      }
    }
  };

  const pageTitle = categoryName
    ? `${t('menu_items_for')} "${categoryName}"`
    : t('admin_menu_management_title');

  return (
    <>
      <div className={styles.adminContainer}>
        <PageHeader title={pageTitle}>
          <div className={styles.pageActions}>
            <select onChange={handleCategoryChange} value={selectedCategoryId || 'all'} className={styles.adminSelect}>
              <option value="all">{t('all_categories_nav')}</option>
              {categories.map((category) => (
                <option key={category.id} value={category.id}>{category.name}</option>
              ))}
            </select>
            <div className={styles.tooltipContainer}>
              <button
                className={`${styles.adminButton} ${styles.add}`}
                onClick={() => setIsCreateModalOpen(true)}
              >
                {t('create_new_product')}
              </button>
            </div>
          </div>
        </PageHeader>
        <div className={styles.adminContent}>
          <ProductsTable
            products={products}
            isLoading={isLoading}
            error={error}
            onEdit={handleOpenEditModal}
            onDelete={handleDeleteClick}
          />
        </div>
      </div>
      <CreateProductModal
        isOpen={isCreateModalOpen}
        onClose={() => setIsCreateModalOpen(false)}
        onProductCreated={fetchProducts}
        categoryId={selectedCategoryId}
      />
      {selectedProduct && (
        <EditProductModal
          isOpen={isEditModalOpen}
          onClose={() => setIsEditModalOpen(false)}
          onProductUpdated={() => {
            setIsEditModalOpen(false);
            fetchProducts();
          }}
          product={selectedProduct}
        />
      )}
      <ConfirmationModal
        isOpen={isConfirmationOpen}
        onClose={() => setIsConfirmationOpen(false)}
        onConfirm={handleConfirmDelete}
        message={t('delete_product_confirmation_message')}
      />
      <ResultModal
        isOpen={isResultModalOpen}
        onClose={() => setIsResultModalOpen(false)}
        message={resultModalMessage}
        isSuccess={isResultModalSuccess}
      />
    </>
  );
};

const MenuManagementPage = () => (
  <Suspense fallback={<div>Loading...</div>}>
    <MenuManagementContent />
  </Suspense>
);

export default MenuManagementPage;
