'use client';

import React, { useEffect, useState } from 'react';
import { ProductDetails } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
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
  const [selected, setSelected] = useState<string[]>(product.categories.map((c) => categories.find(cat => cat.name === c.categoryName)?.id || '').filter(Boolean));
  const [primary, setPrimary] = useState<string | ''>(product.categories.find((c) => c.isPrimary) ? categories.find(cat => cat.name === product.categories.find(c => c.isPrimary)?.categoryName)?.id || '' : '');

  useEffect(() => {
    const fetchAll = async () => {
      const resp = await getCategories();
      if (resp.success) setCategories(resp.data.items);
    };
    fetchAll();
  }, []);

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
          <div className={detailsStyles.checkboxRow}>
            {categories.map(c => (
              <label key={c.id}><input type="checkbox" checked={selected.includes(c.id)} onChange={e=>toggle(c.id, e.target.checked)} /> {c.name}</label>
            ))}
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
