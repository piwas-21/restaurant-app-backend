'use client';

import React, { useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { useProductDetails } from '@/hooks/useProductDetails';
import styles from '@/app/styles/AdminPage.module.css';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import { useTranslation } from 'react-i18next';
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

const ProductDetailsPage = () => {
  const { t } = useTranslation();
  const params = useParams();
  const router = useRouter();
  const productId = params.productId as string;

  const { product, isLoading, error, fetchProductData } = useProductDetails(productId);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isConfirmationOpen, setIsConfirmationOpen] = useState(false);
  const [isResultModalOpen, setIsResultModalOpen] = useState(false);
  const [resultModalMessage, setResultModalMessage] = useState('');
  const [isResultModalSuccess, setIsResultModalSuccess] = useState(false);

  const handleConfirmDelete = async () => {
    if (product) {
      const response = await deleteProduct(product.id);
      setIsConfirmationOpen(false);
      setResultModalMessage(response.data || response.message);
      setIsResultModalSuccess(response.success);
      setIsResultModalOpen(true);
      if (response.success) {
        router.push('/admin/menu-management');
      }
    }
  };

  if (isLoading) return <div className={styles.adminContainer}><p>{t('loading_product_details')}</p></div>;
  if (error) return <div className={styles.adminContainer}><p className={styles.error}>{error}</p></div>;
  if (!product) return <div className={styles.adminContainer}><p>{t('product_not_found')}</p></div>;

  return (
    <>
      <div className={styles.adminContainer}>
        <PageHeader title={product.name}>
          <div className={styles.pageActions}>
            <button className={`${styles.adminButton} ${styles.edit}`} onClick={() => setIsEditModalOpen(true)}>{t('edit_product')}</button>
            <button className={`${styles.adminButton} ${styles.delete}`} onClick={() => setIsConfirmationOpen(true)}>{t('delete_product')}</button>
          </div>
        </PageHeader>
        <div className={`${styles.adminContent} ${detailsStyles.detailsContainer}`}>
          <div className={detailsStyles.mainContent}>
            <ProductInformation product={product} onUpdated={fetchProductData} />
            <DetailsEditor product={product} onUpdated={fetchProductData} />
            <CategoriesEditor product={product} onUpdated={fetchProductData} />
            <MultilingualContentEditor product={product} onUpdated={fetchProductData} />
            <VariationsTable variations={product.variations} productId={product.id} onUpdated={fetchProductData} product={product} />
            <SuggestedSideItemsTable suggestedSideItems={product.suggestedSideItems} productId={product.id} onUpdated={fetchProductData} product={product} />
          </div>
          <ImageGallery images={product.images} productName={product.name} onImageUpdate={fetchProductData} />
        </div>
      </div>
      <EditProductModal
        isOpen={isEditModalOpen}
        onClose={() => setIsEditModalOpen(false)}
        onProductUpdated={() => {
          setIsEditModalOpen(false);
          fetchProductData();
        }}
        product={product}
      />
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

export default ProductDetailsPage;
