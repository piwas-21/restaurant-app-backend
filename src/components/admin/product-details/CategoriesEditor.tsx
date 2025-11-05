'use client';

import React, { useEffect, useState } from 'react';
import { ProductDetails } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import styles from '@/app/styles/AdminPage.module.css';
import { useTranslation } from 'react-i18next';
import { getCategories } from '@/services/categoryService';
import { updateProduct } from '@/services/productService';
import { buildProductPayload } from './buildProductPayload';

interface Category { id: string; name: string; }

interface Props {
  product: ProductDetails;
  onUpdated?: () => void;
}

const CategoriesEditor: React.FC<Props> = ({ product, onUpdated }) => {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selected, setSelected] = useState<string[]>([]);
  const [primary, setPrimary] = useState<string>('');

  useEffect(() => {
    const fetchAll = async () => {
      // Fetch all categories for selection
      const resp = await getCategories(1, 100) as { success: boolean; data?: { items: any[] } };
      if (resp.success && resp.data?.items) {
        setCategories(resp.data.items);

        // Set selected categories after fetching categories
        const selectedIds = product.categories.map((c) =>
          resp.data!.items.find((cat: Category) => cat.name === c.categoryName)?.id || ''
        ).filter(Boolean);
        setSelected(selectedIds);

        // Set primary category after fetching categories
        const primaryCategory = product.categories.find((c) => c.isPrimary);
        if (primaryCategory) {
          const primaryId = resp.data!.items.find((cat: Category) => cat.name === primaryCategory.categoryName)?.id || '';
          setPrimary(primaryId);
        }
      }
    };
    fetchAll();
  }, [product.categories]);

  const toggle = (id: string, checked: boolean) => {
    setSelected(prev => checked ? Array.from(new Set([...(prev||[]), id])) : (prev||[]).filter(x=>x!==id));
  };

  const save = async () => {
    const updated: ProductDetails = {
      ...product,
      categories: selected.map(id => ({
        categoryName: categories.find(c => c.id === id)?.name || '',
        isPrimary: id === primary
      })),
    };
    const payload = {
      ...buildProductPayload(updated),
      categoryIds: selected,
      primaryCategoryId: primary || '',
    };
    await updateProduct(product.id, payload);
    setEditing(false);
    onUpdated && onUpdated();
  };

  return (
    <div className={detailsStyles.infoSection}>
      <div className={detailsStyles.headerRow}>
        <h3>{t('categories')}</h3>
        {!editing ? (
          <button className={`${styles.adminButton} ${styles.edit}`} onClick={()=>setEditing(true)}>{t('edit')}</button>
        ) : (
          <div className={detailsStyles.actionRow}>
            <button className={`${styles.adminButton} ${styles.save}`} onClick={save}>{t('save')}</button>
            <button className={styles.cancelButton} onClick={()=>setEditing(false)}>{t('cancel')}</button>
          </div>
        )}
      </div>
      {!editing ? (
        <ul>
          {product.categories.map(cat => (
            <li key={cat.categoryName}>{cat.categoryName} {cat.isPrimary && `(${t('primary')})`}</li>
          ))}
        </ul>
      ) : (
        <div className={detailsStyles.formGrid}>
          <div className={detailsStyles.formGroup}>
            <label>{t('categories')}</label>
            <div className={modalStyles.chipGroup}>
              {categories.map(c => (
                <div key={c.id} className={modalStyles.chip}>
                  <input
                    type="checkbox"
                    id={`category-inline-${c.id}`}
                    checked={selected.includes(c.id)}
                    onChange={e => toggle(c.id, e.target.checked)}
                  />
                  <label htmlFor={`category-inline-${c.id}`}>{c.name}</label>
                </div>
              ))}
            </div>
          </div>
          <div className={detailsStyles.formGroup}>
            <label>{t('primary_category')}</label>
            <select value={primary} onChange={e=>setPrimary(e.target.value)} disabled={selected.length===0}>
              <option value="" disabled>{t('select_primary_category')}</option>
              {categories.filter(c=>selected.includes(c.id)).map(c=> (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </div>
        </div>
      )}
    </div>
  );
};

export default CategoriesEditor;
