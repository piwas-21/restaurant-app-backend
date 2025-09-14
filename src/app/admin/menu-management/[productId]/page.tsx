'use client';

import React, { useState } from 'react';
import { useParams } from 'next/navigation';
import { useProductDetails } from '@/hooks/useProductDetails';
import styles from '@/app/styles/AdminPage.module.css';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import { useTranslation } from 'react-i18next';
import EditProductModal from '@/components/admin/EditProductModal';
import ImageGallery from '@/components/admin/product-details/ImageGallery';
import ProductInformation from '@/components/admin/product-details/ProductInformation';
import ProductDetailsGrid from '@/components/admin/product-details/ProductDetailsGrid';
import VariationsTable from '@/components/admin/product-details/VariationsTable';
import SuggestedSideItemsTable from '@/components/admin/product-details/SuggestedSideItemsTable';

const ProductDetailsPage = () => {
  const { t } = useTranslation();
  const params = useParams();
  const productId = params.productId as string;

  const { product, isLoading, error, fetchProductData } = useProductDetails(productId);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);

  if (isLoading) return <div className={styles.adminContainer}><p>{t('loading_product_details')}</p></div>;
  if (error) return <div className={styles.adminContainer}><p className={styles.error}>{error}</p></div>;
  if (!product) return <div className={styles.adminContainer}><p>{t('product_not_found')}</p></div>;

  return (
    <>
      <div className={styles.adminContainer}>
        <div className={styles.adminHeader}>
          <h1>{product.name}</h1>
          <button className={styles.adminButton} onClick={() => setIsEditModalOpen(true)}>{t('edit_product')}</button>
        </div>
        <div className={`${styles.adminContent} ${detailsStyles.detailsContainer}`}>
          <ImageGallery images={product.images} productName={product.name} />
          <div className={detailsStyles.mainContent}>
            <ProductInformation product={product} />
            <ProductDetailsGrid product={product} />
            <VariationsTable variations={product.variations} />
            <SuggestedSideItemsTable suggestedSideItems={product.suggestedSideItems} />
          </div>
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
    </>
  );
};

export default ProductDetailsPage;
