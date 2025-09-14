'use client';

import React from 'react';
import { ProductDetails } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import { useTranslation } from 'react-i18next';

interface ProductInformationProps {
  product: ProductDetails;
}

const ProductInformation: React.FC<ProductInformationProps> = ({ product }) => {
  const { t } = useTranslation();

  return (
    <div className={detailsStyles.infoSection}>
      <h2>{t('product_information')}</h2>
      <p><strong>{t('description')}:</strong> {product.description}</p>
      <p><strong>{t('base_price')}:</strong> ${product.basePrice.toFixed(2)}</p>
      <p><strong>{t('status')}:</strong> {product.isActive ? t('active') : t('inactive')} | {product.isAvailable ? t('available') : t('unavailable')}</p>
      <p><strong>{t('type')}:</strong> {t(`product_type_${product.type}`)}</p>
      <p><strong>{t('prep_time')}:</strong> {product.preparationTimeMinutes} {t('minutes')}</p>
    </div>
  );
};

export default ProductInformation;
