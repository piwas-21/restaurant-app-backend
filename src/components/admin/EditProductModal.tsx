'use client';

import React, { useState, useEffect } from 'react';
import { useForm, useFieldArray, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import styles from '@/app/styles/AdminPage.module.css';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import { useTranslation } from 'react-i18next';
import { updateProduct, uploadBulkProductImages } from '@/services/productService';
import { getCategories } from '@/services/categoryService';

// Constants
const productTypes = ['mainItem', 'sideItem', 'beverage', 'dessert', 'sauce', 'addOn'] as const;
const supportedLanguages = ["en", "tr", "es", "ar", "de", "fr", "it"];
const allergensList = ["halal", "vegan", "vegetarian", "gluten-free", "contains_dairy", "contains_nuts"];

// Zod Schema
const variationSchema = z.object({
  id: z.string().optional(),
  name: z.string(),
  description: z.string().optional(),
  priceModifier: z.coerce.number(),
  isActive: z.boolean(),
  displayOrder: z.coerce.number().int(),
});

const contentSchema = z.object({
  language: z.string().min(1, 'Language is required'),
  name: z.string().min(1, 'Name is required for this language'),
  description: z.string().nullish(),
});

const editProductSchema = z.object({
  name: z.string().min(1),
  description: z.string().optional(),
  basePrice: z.coerce.number().min(0),
  isActive: z.boolean(),
  isAvailable: z.boolean(),
  isSpecial: z.boolean(),
  preparationTimeMinutes: z.coerce.number().optional(),
  type: z.enum(productTypes),
  ingredients: z.string().optional(),
  allergens: z.array(z.string()),
  displayOrder: z.coerce.number().optional(),
  categoryIds: z.array(z.string()),
  primaryCategoryId: z.string(),
  variations: z.array(variationSchema),
  content: z.array(contentSchema)
}).refine(d => !d.categoryIds || d.categoryIds.length === 0 || !!d.primaryCategoryId, {
  path: ['primaryCategoryId'],
  message: 'Primary category is required when categories are selected',
});
type EditProductFormValues = z.infer<typeof editProductSchema>;

// Component Props
interface Category { id: string; name: string; }
interface EditProductModalProps {
  isOpen: boolean;
  onClose: () => void;
  onProductUpdated: () => void;
  product: any;
}

const EditProductModal: React.FC<EditProductModalProps> = ({ isOpen, onClose, onProductUpdated, product }) => {
  const { t } = useTranslation();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);
  const [imageFiles, setImageFiles] = useState<File[]>([]);

  const form = useForm<z.infer<typeof editProductSchema>>({
    resolver: zodResolver(editProductSchema),
    defaultValues: {
      name: '',
      description: '',
      basePrice: 0,
      isActive: true,
      isAvailable: true,
      isSpecial: false,
      type: 'mainItem',
      allergens: [],
      ingredients: '',
      variations: [],
      content: [],
      categoryIds: [],
      primaryCategoryId: '',
    }
  });

  const { register, control, formState: { errors }, setError, reset, watch } = form;

  useEffect(() => {
    if (product) {
      const flattenedContent = product.content ? Object.entries(product.content).map(([lang, data]: [string, any]) => ({
        language: lang,
        name: data.name,
        description: data.description,
      })) : [];

      const safeCategoryIds = (product.categories?.map((c: any) => c.categoryId).filter((x: any) => !!x) || []) as string[];
      const safePrimaryId = product.categories?.find((c: any) => c.isPrimary)?.categoryId || '';

      const mappedVariations = (product.variations || []).map((v: any) => ({
        id: v.id,
        name: v.name || '',
        description: v.description ?? '',
        priceModifier: typeof v.priceModifier === 'number' ? v.priceModifier : 0,
        isActive: v.isActive ?? true,
        displayOrder: v.displayOrder ?? 0,
      }));

      reset({
        ...product,
        ingredients: product.ingredients?.join(', '),
        allergens: product.allergens || [],
        categoryIds: safeCategoryIds,
        primaryCategoryId: safePrimaryId,
        content: flattenedContent,
        variations: mappedVariations,
      });
    }
  }, [product, reset]);

  const { fields: variationFields, append: appendVariation, remove: removeVariation } = useFieldArray({ control, name: 'variations' });
  const { fields: contentFields, append: appendContent, remove: removeContent } = useFieldArray({ control, name: 'content' });

  useEffect(() => {
    if (isOpen) {
      const fetchAllCategories = async () => {
        const response = await getCategories();
        if (response.success) setCategories(response.data.items);
      };
      fetchAllCategories();
    }
  }, [isOpen]);

  const onSubmit = async (data: EditProductFormValues) => {
    setIsSubmitting(true);
    const parseNum = (v: any, fallback = 0) => {
      if (typeof v === 'number' && Number.isFinite(v)) return v;
      if (typeof v === 'string') {
        const n = parseFloat(v.replace(',', '.'));
        return Number.isFinite(n) ? n : fallback;
      }
      return fallback;
    };

    const cleanedContentArray = (data.content || [])
      .filter((e: any) => e && e.language && (e.name || '').trim().length > 0)
      .map((e: any) => ({
        language: String(e.language).trim(),
        name: String(e.name || '').trim(),
        description: (e.description ?? '').toString(),
      }));

    const formattedContent = cleanedContentArray.length > 0
      ? cleanedContentArray.reduce((acc: any, curr: any) => {
          acc[curr.language] = { name: curr.name, description: curr.description };
          return acc;
        }, {})
      : undefined;

    const categoryIds = Array.isArray(data.categoryIds) ? data.categoryIds.filter(Boolean) as string[] : [];
    let primaryCategoryId = (data.primaryCategoryId || '') as string;
    if (categoryIds.length > 0 && !categoryIds.includes(primaryCategoryId)) {
      primaryCategoryId = categoryIds[0];
    }

    const cleanedVariations = (data.variations || [])
      .filter(v => (v?.name || '').trim().length > 0)
      .map(v => ({
        id: v.id,
        name: (v.name || '').trim(),
        description: v.description ?? '',
        priceModifier: parseNum(v.priceModifier, 0),
        isActive: v.isActive ?? true,
        displayOrder: Number.isInteger(v.displayOrder as any) ? (v.displayOrder as any) : 0,
      }));

    const productData = {
      ...data,
      id: product.id,
      name: (data.name || '').trim(),
      description: (data.description ?? '').toString(),
      basePrice: parseNum(data.basePrice, 0),
      preparationTimeMinutes: typeof data.preparationTimeMinutes === 'number' ? data.preparationTimeMinutes : parseInt(String(data.preparationTimeMinutes || '0'), 10) || 0,
      type: productTypes.includes(data.type as any) ? data.type : product.type,
      ingredients: data.ingredients ? data.ingredients.split(',').map(s => s.trim()).filter(Boolean) : [],
      allergens: Array.isArray(data.allergens) ? data.allergens.filter(Boolean) : [],
      categoryIds,
      primaryCategoryId,
      variations: cleanedVariations,
      content: formattedContent,
    } as any;

    try {
      const response = await updateProduct(product.id, productData);
      if (response.success) {
        if (imageFiles.length > 0) {
          await uploadBulkProductImages(product.id, imageFiles);
        }
        onProductUpdated();
        onClose();
      } else {
        setError('root', { message: response.message || 'Failed to update product' });
      }
    } catch {
      setError('root', { message: 'An unexpected error occurred.' });
    } finally {
      setIsSubmitting(false);
    }
  }

  if (!isOpen) return null;

  const getErrorMessages = () => {
    const msgs: string[] = [];
    const walk = (obj: any, path: string[] = []) => {
      if (!obj || typeof obj !== 'object') return;
      if (obj.type === 'error' && obj.message) {
        msgs.push(`${path.join('.')} — ${obj.message}`);
      }
      for (const key of Object.keys(obj)) {
        const val: any = obj[key as keyof typeof obj];
        if (val && typeof val === 'object') {
          walk(val, path.concat(key));
        }
      }
    };
    walk(errors as any);
    return Array.from(new Set(msgs)).filter(Boolean).slice(0, 8);
  };

  return (
    <div className={modalStyles.modalOverlay}>
      <div className={modalStyles.modalContent}>
        <div className={modalStyles.modalHeader}>
          <h2>{t('edit_product')}</h2>
        </div>
        <form onSubmit={form.handleSubmit(onSubmit)}>
          <div className={modalStyles.formGrid}>
            <div className={modalStyles.formColumn}>
              <div className={modalStyles.formGroup}><label>{t('product_name')}</label><input className={errors.name ? modalStyles.fieldError : ''} {...register('name')} /></div>
              <div className={modalStyles.formGroup}><label>{t('description')}</label><textarea {...register('description')} /></div>
              <div className={modalStyles.formGroup}><label>{t('ingredients')}</label><input {...register('ingredients')} /></div>
            </div>
          <div className={modalStyles.formColumn}>
            <div className={modalStyles.formGroup}><label>{t('base_price')}</label><input type="number" step="0.01" className={errors.basePrice ? modalStyles.fieldError : ''} {...register('basePrice')} /></div>
            <div className={modalStyles.formGroup}>
              <label>{t('product_type')}</label>
              <select className={errors.type ? modalStyles.fieldError : ''} {...register('type')}>
                {productTypes.map(type => <option key={type} value={type}>{t(`product_type_${type}`)}</option>)}
              </select>
            </div>

            <div className={modalStyles.chipGroup}>
              <div className={modalStyles.chip}>
                <input type="checkbox" id="e-product-active" {...register('isActive')} />
                <label htmlFor="e-product-active">{t('active')}</label>
              </div>
              <div className={modalStyles.chip}>
                <input type="checkbox" id="e-product-available" {...register('isAvailable')} />
                <label htmlFor="e-product-available">{t('available')}</label>
              </div>
              <div className={modalStyles.chip}>
                <input type="checkbox" id="e-product-special" {...register('isSpecial')} />
                <label htmlFor="e-product-special">{t('special_of_the_day_title') || 'Special of the day'}</label>
              </div>
            </div>

            <div className={modalStyles.formGroup}>
              <label>{t('product_images')} ({t('optional')})</label>
              <input type="file" multiple onChange={(e) => setImageFiles(Array.from(e.target.files || []))} />
              {imageFiles.length > 0 && <p>{t('files_selected', { count: imageFiles.length })}</p>}
            </div>
            </div>
          </div>

          <div className={modalStyles.fullWidth}>
            <div className={modalStyles.formGroup}>
              <h3>{t('categories')}</h3>
              <Controller
                name="categoryIds"
                control={control}
                render={({ field }) => (
                  <div className={modalStyles.chipGroup}>
                    {categories.map(cat => (
                      <div key={cat.id} className={modalStyles.chip}>
                        <input
                          type="checkbox"
                          id={`category-chip-${cat.id}`}
                          value={cat.id}
                          checked={field.value?.includes(cat.id)}
                          onChange={e => {
                            const selectedIds = field.value || [];
                            field.onChange(e.target.checked ? [...selectedIds, cat.id] : selectedIds.filter((id: string) => id !== cat.id));
                          }}
                        />
                        <label htmlFor={`category-chip-${cat.id}`}>{cat.name}</label>
                      </div>
                    ))}
                  </div>
                )}
              />
            </div>

            <div className={modalStyles.formGroup}>
              <label>{t('primary_category')}</label>
              <select className={errors.primaryCategoryId ? modalStyles.fieldError : ''} {...register('primaryCategoryId')} disabled={!watch('categoryIds') || watch('categoryIds')?.length === 0}>
                <option value="" disabled>{t('select_primary_category')}</option>
                {categories.filter(cat => (watch('categoryIds') || []).includes(cat.id)).map(cat => (
                  <option key={cat.id} value={cat.id}>{cat.name}</option>
                ))}
              </select>
              {errors.primaryCategoryId && <div className={modalStyles.errorMessage}>{errors.primaryCategoryId.message as any}</div>}
            </div>

            <div className={modalStyles.formGroup}>
              <h3>{t('allergens')}</h3>
              <Controller
                name="allergens"
                control={control}
                render={({ field }) => {
                  const selected: string[] = Array.isArray(field.value) ? field.value : [];
                  return (
                    <div className={modalStyles.chipGroup}>
                      {allergensList.map(allergen => (
                        <div key={allergen} className={modalStyles.chip}>
                          <input
                            type="checkbox"
                            id={`allergen-chip-${allergen}`}
                            value={allergen}
                            checked={selected.includes(allergen)}
                            onChange={e => {
                              const next = e.target.checked
                                ? [...selected, allergen]
                                : selected.filter((a: string) => a !== allergen);
                              field.onChange(next);
                            }}
                          />
                          <label htmlFor={`allergen-chip-${allergen}`}>{t(`allergen_${allergen}`)}</label>
                        </div>
                      ))}
                    </div>
                  );
                }}
              />
            </div>
            <div className={modalStyles.formGroup}>
              <h3>{t('variations')}</h3>
              {variationFields.map((field, index) => (
                <div key={field.id} className={modalStyles.variationItem}>
                  <div className={modalStyles.formGroup}><label>{t('variation_name')}</label><input {...register(`variations.${index}.name`)} /></div>
                  <div className={modalStyles.formGroup}><label>{t('price_modifier')}</label><input type="number" step="0.01" {...register(`variations.${index}.priceModifier`)} /></div>
                  <button type="button" className={modalStyles.cancelButton} onClick={() => removeVariation(index)}>{t('remove')}</button>
                </div>
              ))}
              <button type="button" className={`${styles.adminButton} ${modalStyles.addSectionButton}`} onClick={() => appendVariation({ name: '', priceModifier: 0, isActive: true, displayOrder: 0 })}>{t('add_variation')}</button>
            </div>

            <div className={modalStyles.formGroup}>
              <h3>{t('multilingual_content')}</h3>
              {contentFields.map((field, index) => (
                <div key={field.id} className={modalStyles.contentItem}>
                  <div className={styles.gridItem}>
                    {(() => {
                      const all = (watch('content') || []) as any[];
                      const usedLangs = all.map(e => e.language).filter(Boolean);
                      const currentLang = (watch(`content.${index}.language`) as any) || '';
                      return (
                        <select {...register(`content.${index}.language`)}>
                          <option value="" disabled>Language</option>
                          {supportedLanguages.map(l => (
                            <option key={l} value={l} disabled={usedLangs.includes(l) && l !== currentLang}>{t(`lang_${l}`)}</option>
                          ))}
                        </select>
                      );
                    })()}
                    <input {...register(`content.${index}.name`)} placeholder={t('name_in_language')} />
                    <textarea {...register(`content.${index}.description`)} placeholder={t('ingredients_in_language')} />
                  </div>
                  <button type="button" className={modalStyles.cancelButton} onClick={() => removeContent(index)}>{t('remove')}</button>
                </div>
              ))}
              <button
                type="button"
                className={`${styles.adminButton} ${modalStyles.addSectionButton}`}
                onClick={() => {
                  const all = (watch('content') || []) as any[];
                  const usedLangs = all.map(e => e.language).filter(Boolean);
                  const next = supportedLanguages.find(l => !usedLangs.includes(l)) || '';
                  appendContent({ language: next as any, name: '', description: '' });
                }}
              >
                {t('add_language_translation')}
              </button>
            </div>
          </div>

          <div className={modalStyles.buttonGroup}>
            <button type="submit" className={modalStyles.submitButton} disabled={isSubmitting}>{isSubmitting ? t('updating...') : t('update_product')}</button>
            <button type="button" onClick={onClose} className={modalStyles.cancelButton}>{t('cancel')}</button>
          </div>
          {getErrorMessages().length > 0 && (
            <div className={modalStyles.errorMessage}>
              {getErrorMessages().map((m, i) => (
                <div key={i}>{m}</div>
              ))}
            </div>
          )}
        </form>
      </div>
    </div>
  );
};

export default EditProductModal;
