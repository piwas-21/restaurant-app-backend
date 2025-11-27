'use client';

import React, { useState, useEffect } from 'react';
import { ProductDetails, ProductIngredient } from '@/app/admin/menu-management/interfaces';
import detailsStyles from '@/app/styles/DetailsPage.module.css';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import styles from '@/app/styles/AdminPage.module.css';
import { useTranslation } from 'react-i18next';
import { updateProduct } from '@/services/productService';
import { buildProductPayload } from './buildProductPayload';
import { getCategories } from '@/services/categoryService';
import AllergenDisplay, { AVAILABLE_ALLERGENS } from '@/components/common/AllergenDisplay';
import { ProductIngredientsManager } from '@/components/admin/product/ProductIngredientsManager';

interface Category { id: string; name: string; }

interface Props {
  product: ProductDetails;
  onUpdated?: () => void;
}

const DetailsEditor: React.FC<Props> = ({ product, onUpdated }) => {
  const { t, i18n } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [detailedIngredients, setDetailedIngredients] = useState<ProductIngredient[]>(
    product.detailedIngredients || []
  );
  const [allergens, setAllergens] = useState<string[]>(product.allergens || []);
  const [categories, setCategories] = useState<Category[]>([]);

  const currentLanguage = (i18n.language.split('-')[0] || 'en') as string;

  // Get multilingual ingredients from detailedIngredients with fallback
  const getLocalizedIngredients = () => {
    // Use new detailedIngredients structure
    if (product.detailedIngredients && product.detailedIngredients.length > 0) {
      return product.detailedIngredients
        .filter((ing) => ing.isActive)
        .map((ing) => {
          // Try to get name in current language, fallback to English, then base name
          return ing.content?.[currentLanguage]?.name || ing.content?.en?.name || ing.name;
        })
        .join(', ');
    }

    // Fallback to legacy structures
    return product.content?.[currentLanguage]?.ingredient ||
           product.content?.en?.ingredient ||
           (Array.isArray(product.ingredients) ? product.ingredients.join(', ') : '') ||
           '';
  };

  useEffect(() => {
    const fetchCategories = async () => {
      const resp = await getCategories() as { success: boolean; data?: { items: any[] } };
      if (resp.success && resp.data?.items) {
        setCategories(resp.data.items);
      }
    };
    fetchCategories();
  }, []);

  const toggleAllergen = (a: string, checked: boolean) => {
    setAllergens(prev => checked ? Array.from(new Set([...(prev||[]), a])) : (prev||[]).filter(x=>x!==a));
  };

  const save = async () => {
    const updated: ProductDetails = { ...product, detailedIngredients, allergens } as any;
    const payload = buildProductPayload(updated, categories);
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
          <p>
            <strong>{t('ingredients')}:</strong> {getLocalizedIngredients() || <em>{t('no_ingredients_added')}</em>}
          </p>
          <div className={detailsStyles.inlineBadges} style={{ marginBottom: '1rem', marginTop: '1rem', alignItems: 'flex-start' }}>
            <span style={{ marginTop: '0.25rem' }}><strong>{t('allergens')}:</strong></span>
            <AllergenDisplay
              allergens={product.allergens}
              id="product-details-allergens"
              variant="admin"
              showLabel={false}
            />
          </div>
        </>
      ) : (
        <div className={detailsStyles.formGrid}>
          <div className={detailsStyles.formGroup} style={{ gridColumn: '1 / -1' }}>
            <ProductIngredientsManager
              ingredients={detailedIngredients}
              onChange={setDetailedIngredients}
              productBasePrice={product.basePrice}
            />
          </div>
          <div className={detailsStyles.formGroup}>
            <label>{t('allergens')}</label>
            <div className={modalStyles.chipGroup}>
              {AVAILABLE_ALLERGENS.map(a => (
                <div key={a} className={modalStyles.chip}>
                  <input
                    type="checkbox"
                    id={`allergen-inline-${a}`}
                    checked={allergens.includes(a)}
                    onChange={e => toggleAllergen(a, e.target.checked)}
                  />
                  <label htmlFor={`allergen-inline-${a}`}>{t(`allergen_${a}`)}</label>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default DetailsEditor;

