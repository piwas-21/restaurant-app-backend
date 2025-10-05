'use client';

import React, { useState, useEffect } from 'react';
import { ProductDetails } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';
import { updateProduct } from '@/services/productService';
import { buildProductPayload } from './buildProductPayload';
import { getCategories } from '@/services/categoryService';
import { productTypes } from '../product/types';

interface Category {
  id: string;
  name: string;
}

interface ProductInformationProps {
  product: ProductDetails;
  onUpdated?: () => void;
}

const ProductInformation: React.FC<ProductInformationProps> = ({ product, onUpdated }) => {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);
  const [form, setForm] = useState({
    name: product.name,
    description: product.description,
    basePrice: product.basePrice,
    type: product.type,
    isActive: product.isActive,
    isAvailable: product.isAvailable,
    isSpecial: (product as any) && (product as any).isSpecial ? (product as any).isSpecial : false,
    preparationTimeMinutes: product.preparationTimeMinutes,
  });

  useEffect(() => {
    const fetchCategories = async () => {
      const resp = await getCategories();
      if (resp.success) {
        setCategories(resp.data.items);
      }
    };
    fetchCategories();
  }, []);

  const onSave = async () => {
    const payload = buildProductPayload({ ...product, ...form } as any, categories);
    try {
      const res = await updateProduct(product.id, payload);
      if (res.success) {
        setEditing(false);
        onUpdated && onUpdated();
      }
    } catch {
      // swallow; could add toast
    }
  };

  return (
    <div className={detailsStyles.infoSection}>
      <div className={detailsStyles.headerRow}>
        <h2>{t('product_information')}</h2>
        {!editing ? (
          <button className={`${styles.adminButton} ${styles.edit}`} onClick={() => setEditing(true)}>
            {t('edit')}
          </button>
        ) : (
          <div className={detailsStyles.actionRow}>
            <button className={`${styles.adminButton} ${styles.save}`} onClick={onSave}>
              {t('save')}
            </button>
            <button className={styles.cancelButton} onClick={() => setEditing(false)}>
              {t('cancel')}
            </button>
          </div>
        )}
      </div>

      {!editing ? (
        <>
          <p>
            <strong>{t('description')}:</strong> {product.description}
          </p>
          <p>
            <strong>{t('base_price')}:</strong> CHF{product.basePrice.toFixed(2)}
          </p>
          <p>
            <strong>{t('status')}:</strong> {product.isActive ? t('active') : t('inactive')} | {' '}
            {product.isAvailable ? t('available') : t('unavailable')} | {' '}
            {(product as any)?.isSpecial && t('special_of_the_day_title')}
          </p>
          <p>
            <strong>{t('type')}:</strong> {t(`product_type_${product.type}`)}
          </p>
          <p>
            <strong>{t('prep_time')}:</strong> {product.preparationTimeMinutes} {t('minutes')}
          </p>
        </>
      ) : (
        <div className={detailsStyles.formGrid}>
          <div className={detailsStyles.formGroup}>
            <label>{t('product_name')}</label>
            <input value={form.name} onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))} />
          </div>
          <div className={detailsStyles.formGroup}>
            <label>{t('description')}</label>
            <textarea
              value={form.description}
              onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
            />
          </div>
          <div className={detailsStyles.formGroup}>
            <label>{t('base_price')}</label>
            <input
              type="number"
              step="0.01"
              value={form.basePrice}
              onChange={(e) => setForm((f) => ({ ...f, basePrice: parseFloat(e.target.value) }))}
            />
          </div>
          <div className={detailsStyles.formGroup}>
            <label>{t('product_type')}</label>
            <select value={form.type} onChange={(e) => setForm((f) => ({ ...f, type: e.target.value }))}>
              {productTypes.map((type) => (
                <option key={type} value={type}>
                  {t(`product_type_${type}`)}
                </option>
              ))}
            </select>
          </div>

          <div className={detailsStyles.formGroup}>
            <label>{t('prep_time')}</label>
            <input
              type="number"
              value={form.preparationTimeMinutes}
              onChange={(e) => setForm((f) => ({ ...f, preparationTimeMinutes: parseInt(e.target.value || '0', 10) }))}
            />
          </div>

          <div className={detailsStyles.formGroup}>
            <div className={modalStyles.chipGroup}>
              <div className={modalStyles.chip}>
                <input
                  type="checkbox"
                  id="product-active-inline"
                  checked={form.isActive}
                  onChange={(e) => setForm((f) => ({ ...f, isActive: e.target.checked }))}
                />
                <label htmlFor="product-active-inline">{t('active')}</label>
              </div>
              <div className={modalStyles.chip}>
                <input
                  type="checkbox"
                  id="product-available-inline"
                  checked={form.isAvailable}
                  onChange={(e) => setForm((f) => ({ ...f, isAvailable: e.target.checked }))}
                />
                <label htmlFor="product-available-inline">{t('available')}</label>
              </div>
              <div className={modalStyles.chip}>
                <input
                  type="checkbox"
                  id="product-special-inline"
                  checked={form.isSpecial}
                  onChange={(e) => setForm((f) => ({ ...f, isSpecial: e.target.checked }))}
                />
                <label htmlFor="product-special-inline">{t('special_of_the_day_title')}</label>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default ProductInformation;
