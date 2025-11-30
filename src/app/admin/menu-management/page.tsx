'use client';

import React, { useState, Suspense } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'next/navigation';
import { useMenuManagement } from '@/hooks/useMenuManagement';
import { getProductById, deleteMenuBundle, getMenuBundleById } from '@/services/menuService';
import { deleteProduct } from '@/services/productService';
import styles from '@/app/styles/AdminPage.module.css';
import CreateProductModal from '@/components/admin/CreateProductModal';
import CreateMenuBundleModal from '@/components/admin/CreateMenuBundleModal';
import EditProductModal from '@/components/admin/EditProductModal';
import EditMenuBundleModal from '@/components/admin/EditMenuBundleModal';
import PageHeader from '@/components/admin/PageHeader';
import ProductsTable from '@/components/admin/menu-management/ProductsTable';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import Pagination from '@/components/common/Pagination';
import { AdminAuthGuard } from '@/components/admin/AdminAuthGuard';

const MenuManagementContent = () => {
  const { t } = useTranslation();
  const searchParams = useSearchParams();
  const categoryName = searchParams.get('categoryName');
  const [activeTab, setActiveTab] = useState<'products' | 'menus'>('products');

  const {
    products,
    categories,
    selectedCategoryId,
    isLoading,
    error,
    currentPage,
    totalPages,
    totalCount,
    pageSize,
    handleCategoryChange,
    handlePageChange,
    fetchProducts,
  } = useMenuManagement(activeTab);

  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isCreateMenuModalOpen, setIsCreateMenuModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isEditMenuModalOpen, setIsEditMenuModalOpen] = useState(false);
  const [selectedProduct, setSelectedProduct] = useState<any | null>(null);
  const [isConfirmationOpen, setIsConfirmationOpen] = useState(false);
  const [productToDelete, setProductToDelete] = useState<string | null>(null);
  const [isResultModalOpen, setIsResultModalOpen] = useState(false);
  const [resultModalMessage, setResultModalMessage] = useState('');
  const [isResultModalSuccess, setIsResultModalSuccess] = useState(false);

  const handleOpenEditModal = async (productId: string) => {
    try {
      let response;
      if (activeTab === 'menus') {
        response = await getMenuBundleById(productId) as { success: boolean; data?: any; message?: string };
      } else {
        response = await getProductById(productId) as { success: boolean; data?: any; message?: string };
      }

      if (response.success) {
        setSelectedProduct(response.data);
        if (response.data.type === 'menu') {
            setIsEditMenuModalOpen(true);
        } else {
            setIsEditModalOpen(true);
        }
      } else {
        // Handle error
      }
    } catch {
      // Handle error
    }
  };

  const handleDeleteClick = (productId: string) => {
    setProductToDelete(productId);
    setIsConfirmationOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (productToDelete) {
      let response;
      // We need to know if it's a menu bundle or product to call the right API
      // Since we only have ID here, we might need to check the current tab or fetch details first.
      // However, for delete, we can try to infer from the active tab.
      if (activeTab === 'menus') {
         response = await deleteMenuBundle(productToDelete) as { success: boolean; message?: string; data?: string };
      } else {
         response = await deleteProduct(productToDelete) as { success: boolean; message?: string; data?: string };
      }
      
      setIsConfirmationOpen(false);
      setResultModalMessage(response.data || response.message || '');
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
            <div className={styles.tabs}>
              <button
                className={`${styles.tabButton} ${activeTab === 'products' ? styles.activeTab : ''}`}
                onClick={() => setActiveTab('products')}
              >
                {t('products')}
              </button>
              <button
                className={`${styles.tabButton} ${activeTab === 'menus' ? styles.activeTab : ''}`}
                onClick={() => setActiveTab('menus')}
              >
                {t('menu_bundles')}
              </button>
            </div>

            {/* Category filter - only show for Products tab */}
            {activeTab === 'products' && (
              <select onChange={handleCategoryChange} value={selectedCategoryId || 'all'} className={styles.adminSelect}>
                <option value="all">{t('all_categories_nav')}</option>
                {categories.map((category) => (
                  <option key={category.id} value={category.id}>{category.name}</option>
                ))}
              </select>
            )}
            <div className={styles.tooltipContainer}>
              {activeTab === 'products' ? (
                <button
                  className={`${styles.adminButton} ${styles.add}`}
                  onClick={() => setIsCreateModalOpen(true)}
                >
                  {t('create_new_product')}
                </button>
              ) : (
                <button
                  className={`${styles.adminButton} ${styles.add}`}
                  onClick={() => setIsCreateMenuModalOpen(true)}
                >
                  {t('create_menu_bundle')}
                </button>
              )}
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

          {/* Pagination */}
          {!isLoading && products.length > 0 && (
            <>
              <Pagination
                currentPage={currentPage}
                totalPages={totalPages}
                onPageChange={handlePageChange}
                isLoading={isLoading}
              />

              {/* Pagination Info */}
              {totalCount > 0 && (
                <p style={{ textAlign: 'center', marginTop: '1rem', color: '#666' }}>
                  {t('showing_items', {
                    start: (currentPage - 1) * pageSize + 1,
                    end: Math.min(currentPage * pageSize, totalCount),
                    total: totalCount,
                    defaultValue: `Showing ${(currentPage - 1) * pageSize + 1}-${Math.min(currentPage * pageSize, totalCount)} of ${totalCount} items`
                  })}
                </p>
              )}
            </>
          )}
        </div>
      </div>
      <CreateProductModal
        isOpen={isCreateModalOpen}
        onClose={() => setIsCreateModalOpen(false)}
        onProductCreated={fetchProducts}
        categoryId={selectedCategoryId}
      />
      <CreateMenuBundleModal
        isOpen={isCreateMenuModalOpen}
        onClose={() => setIsCreateMenuModalOpen(false)}
        onProductCreated={fetchProducts}
        categoryId={selectedCategoryId}
      />
      {selectedProduct && selectedProduct.type === 'menu' ? (
        <EditMenuBundleModal
          isOpen={isEditMenuModalOpen}
          onClose={() => setIsEditMenuModalOpen(false)}
          onProductUpdated={() => {
            setIsEditMenuModalOpen(false);
            fetchProducts();
          }}
          product={selectedProduct}
        />
      ) : selectedProduct && (
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
  <AdminAuthGuard>
    <Suspense fallback={<div>Loading...</div>}>
      <MenuManagementContent />
    </Suspense>
  </AdminAuthGuard>
);

export default MenuManagementPage;
