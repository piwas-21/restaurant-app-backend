'use client';

import React, { useState } from 'react';
import { ProductDetails } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import styles from '@/app/styles/AdminPage.module.css';
import { useTranslation } from 'react-i18next';
import { updateProduct } from '@/services/productService';
import { buildProductPayload } from './buildProductPayload';

const allergensList = ["halal", "vegan", "vegetarian", "gluten-free", "contains_dairy", "contains_nuts"];

interface Props {
  product: ProductDetails;
  onUpdated?: () => void;
}

const DetailsEditor: React.FC<Props> = ({ product, onUpdated }) => {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [ingredients, setIngredients] = useState(product.ingredients.join(', '));
  const [allergens, setAllergens] = useState<string[]>(product.allergens || []);

  const toggleAllergen = (a: string, checked: boolean) => {
    setAllergens(prev => checked ? Array.from(new Set([...(prev||[]), a])) : (prev||[]).filter(x=>x!==a));
  };

  const save = async () => {
    const updated: ProductDetails = { ...product, ingredients: ingredients ? ingredients.split(',').map(s=>s.trim()).filter(Boolean) : [], allergens } as any;
    const payload = buildProductPayload(updated);
    await updateProduct(product.id, payload);
    setEditing(false);
    onUpdated && onUpdated();
  };

  return (
    <div className={detailsStyles.infoSection}>
      <div className={detailsStyles.headerRow}>
        <h3>{t('details')}</h3>
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
        <>
          <p><strong>{t('ingredients')}:</strong> {product.ingredients.join(', ')}</p>
          <p><strong>{t('allergens')}:</strong> {product.allergens.map(a => t(`allergen_${a}`)).join(', ')}</p>
        </>
      ) : (
        <div className={detailsStyles.formGrid}>
          <div className={detailsStyles.formGroup}>
            <label>{t('ingredients')}</label>
            <input value={ingredients} onChange={e=>setIngredients(e.target.value)} />
          </div>
          <div className={detailsStyles.formGroup}>
            <label>{t('allergens')}</label>
            <div className={detailsStyles.checkboxRow}>
              {allergensList.map(a => (
                <label key={a}><input type="checkbox" checked={allergens.includes(a)} onChange={e=>toggleAllergen(a, e.target.checked)} /> {t(`allergen_${a}`)}</label>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default DetailsEditor;

