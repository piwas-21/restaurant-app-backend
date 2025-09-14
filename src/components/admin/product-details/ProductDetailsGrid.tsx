'use client';

import React from 'react';
import { ProductDetails } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import { useTranslation } from 'react-i18next';

interface ProductDetailsGridProps {
  product: ProductDetails;
}

const ProductDetailsGrid: React.FC<ProductDetailsGridProps> = ({ product }) => {
  const { t } = useTranslation();

  return (
    <div className={detailsStyles.infoGrid}>
      <div className={detailsStyles.infoSection}>
        <h3>{t('details')}</h3>
        <p><strong>{t('ingredients')}:</strong> {product.ingredients.join(', ')}</p>
        <p><strong>{t('allergens')}:</strong> {product.allergens.map(a => t(`allergen_${a}`)).join(', ')}</p>
      </div>
      <div className={detailsStyles.infoSection}>
        <h3>{t('categories')}</h3>
        <ul>
          {product.categories.map(cat => (
            <li key={cat.categoryName}>{cat.categoryName} {cat.isPrimary && `(${t('primary')})`}</li>
          ))}
        </ul>
      </div>
    </div>
  );
};

export default ProductDetailsGrid;
