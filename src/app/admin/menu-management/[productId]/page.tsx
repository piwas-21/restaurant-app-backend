'use client';

import React, { useState, useEffect, useCallback } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import styles from '@/app/styles/AdminPage.module.css';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import { useTranslation } from 'react-i18next';
import EditMenuBundleModal from '@/components/admin/EditMenuBundleModal';
import MenuBundleDetails from '@/components/admin/menu-details/MenuBundleDetails';
import { deleteMenuBundle, getMenuBundleById, getProductById } from '@/services/menuService';
import EditProductModal from '@/components/admin/EditProductModal';
import ImageGallery from '@/components/admin/product-details/ImageGallery';
import ProductInformation from '@/components/admin/product-details/ProductInformation';
import DetailsEditor from '@/components/admin/product-details/DetailsEditor';
import CategoriesEditor from '@/components/admin/product-details/CategoriesEditor';
import MultilingualContentEditor from '@/components/admin/product-details/MultilingualContentEditor';
import VariationsTable from '@/components/admin/product-details/VariationsTable';
import SuggestedSideItemsTable from '@/components/admin/product-details/SuggestedSideItemsTable';
import PageHeader from '@/components/admin/PageHeader';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import ResultModal from '@/components/common/ResultModal';
import { deleteProduct } from '@/services/productService';
import { ProductDetails } from '@/app/admin/menu-management/interfaces';

const ProductDetailsPage = () => {
  const { t } = useTranslation();
  const params = useParams();
  const router = useRouter();
  const searchParams = useSearchParams();
  const productId = params.productId as string;
  const type = searchParams.get('type'); // 'menu' or 'product'

  const [product, setProduct] = useState<ProductDetails | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isMenuBundle, setIsMenuBundle] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isConfirmationOpen, setIsConfirmationOpen] = useState(false);
  const [isResultModalOpen, setIsResultModalOpen] = useState(false);
  const [resultModalMessage, setResultModalMessage] = useState('');
  const [isResultModalSuccess, setIsResultModalSuccess] = useState(false);

  const fetchProductData = useCallback(async () => {
    if (!productId) return;

    setIsLoading(true);
    setError(null);
    try {
      let response;

      // Use type parameter to determine which API to call
      if (type === 'menu') {
        response = await getMenuBundleById(productId) as { success: boolean; data?: any; message?: string };
        if (response.success && response.data) {
          setProduct(response.data);
          setIsMenuBundle(true);
        } else {
          setError(response.message || 'Failed to fetch menu bundle details.');
        }
      } else {
        // Default to product API
        response = await getProductById(productId) as { success: boolean; data?: any; message?: string };
        if (response.success && response.data) {
          setProduct(response.data);
          setIsMenuBundle(response.data.type === 'menu');
        } else {
          setError(response.message || 'Failed to fetch product details.');
        }
      }
    } catch {
      setError('An unexpected error occurred.');
    } finally {
      setIsLoading(false);
    }
  }, [productId, type]);

  useEffect(() => {
    fetchProductData();
  }, [fetchProductData]);

  const handleConfirmDelete = async () => {
    if (product) {
      let response;
      if (isMenuBundle) {
        response = await deleteMenuBundle(product.id) as { success: boolean; message?: string; data?: string };
      } else {
        response = await deleteProduct(product.id) as { success: boolean; message?: string; data?: string };
      }

      setIsConfirmationOpen(false);
      setResultModalMessage(response.data || response.message || '');
      setIsResultModalSuccess(response.success);
      setIsResultModalOpen(true);
      if (response.success) {
        router.push('/admin/menu-management');
      }
    }
  };

  const handleUpdate = () => {
    fetchProductData();
  };

  if (isLoading) return <div className={styles.adminContainer}><p>{t('loading_product_details')}</p></div>;
  if (error) return <div className={styles.adminContainer}><p className={styles.error}>{error}</p></div>;
  if (!product) return <div className={styles.adminContainer}><p>{t('product_not_found')}</p></div>;

  return (
    <>
      <div className={styles.adminContainer}>
        <PageHeader title={product.name}>
          <div className={styles.pageActions}>
            <button
              className={`${styles.adminButton} ${styles.edit}`}
              onClick={() => setIsEditModalOpen(true)}
            >
              {isMenuBundle ? t('edit_menu_bundle') : t('edit_product')}
            </button>
            <button
              className={`${styles.adminButton} ${styles.delete}`}
              onClick={() => setIsConfirmationOpen(true)}
            >
              {isMenuBundle ? t('delete_menu_bundle') : t('delete_product')}
            </button>
          </div>
        </PageHeader>

        {isMenuBundle ? (
          <MenuBundleDetails product={product} onUpdated={fetchProductData} />
        ) : (
          <div className={`${styles.adminContent} ${detailsStyles.detailsContainer}`}>
            <div className={detailsStyles.mainContent}>
              <ProductInformation product={product} onUpdated={fetchProductData} />
              <DetailsEditor product={product} onUpdated={fetchProductData} />
              <CategoriesEditor product={product} onUpdated={fetchProductData} />
              <MultilingualContentEditor product={product} onUpdated={fetchProductData} />
              <VariationsTable
                variations={product.variations || []}
                productId={product.id}
                onUpdated={fetchProductData}
                product={product}
              />
              <SuggestedSideItemsTable
                suggestedSideItems={product.suggestedSideItems || []}
                productId={product.id}
                onUpdated={fetchProductData}
                product={product}
              />
            </div>
            <div className={detailsStyles.sidebar}>
              <ImageGallery
                images={product.images || []}
                productName={product.name}
                onImageUpdate={fetchProductData}
              />
            </div>
          </div>
        )}
      </div>

      {isMenuBundle ? (
        <EditMenuBundleModal
          isOpen={isEditModalOpen}
          onClose={() => setIsEditModalOpen(false)}
          onProductUpdated={handleUpdate}
          product={product}
        />
      ) : (
        <EditProductModal
          isOpen={isEditModalOpen}
          onClose={() => setIsEditModalOpen(false)}
          onProductUpdated={handleUpdate}
          product={product}
        />
      )}

      <ConfirmationModal
        isOpen={isConfirmationOpen}
        onClose={() => setIsConfirmationOpen(false)}
        onConfirm={handleConfirmDelete}
        message={isMenuBundle ? t('confirm_delete_menu_bundle_message') : t('confirm_delete_product_message')}
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

export default ProductDetailsPage;
