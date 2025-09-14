'use client';

import React, { useState, Suspense } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'next/navigation';
import { useMenuManagement } from '@/hooks/useMenuManagement';
import { getProductById } from '@/services/menuService';
import styles from '@/app/styles/AdminPage.module.css';
import CreateProductModal from '@/components/admin/CreateProductModal';
import EditProductModal from '@/components/admin/EditProductModal';
import MenuManagementHeader from '@/components/admin/menu-management/MenuManagementHeader';
import ProductsTable from '@/components/admin/menu-management/ProductsTable';

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

  const pageTitle = categoryName
    ? `${t('menu_items_for')} "${categoryName}"`
    : t('admin_menu_management_title');

  return (
    <>
      <div className={styles.adminContainer}>
        <MenuManagementHeader
          pageTitle={pageTitle}
          categories={categories}
          selectedCategoryId={selectedCategoryId}
          onCategoryChange={handleCategoryChange}
          onOpenCreateModal={() => setIsCreateModalOpen(true)}
        />
        <div className={styles.adminContent}>
          <ProductsTable
            products={products}
            isLoading={isLoading}
            error={error}
            onEdit={handleOpenEditModal}
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
    </>
  );
};

const MenuManagementPage = () => (
  <Suspense fallback={<div>Loading...</div>}>
    <MenuManagementContent />
  </Suspense>
);

export default MenuManagementPage;
