'use client';

import React, { useState, useEffect } from 'react';
import { useForm, useFieldArray, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import styles from '@/app/styles/AdminPage.module.css';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import { useTranslation } from 'react-i18next';
import { createProduct, CreateProductData } from '@/services/menuService';
import { getCategories } from '@/services/categoryService';

// Enums and Constants
const productTypes = ["mainItem", "sideItem", "beverage", "dessert", "sauce", "addOn"];
const allergensList = ["halal", "vegan", "vegetarian", "gluten-free", "contains_dairy", "contains_nuts"];
const supportedLanguages = ["en", "tr", "es", "ar", "de", "fr", "it"];

// Zod Schema for Validation
const variationSchema = z.object({
  name: z.string().min(1, 'Variation name is required'),
  description: z.string().optional(),
  priceModifier: z.coerce.number(),
  isActive: z.boolean().default(true),
  displayOrder: z.coerce.number().int().default(0),
});

const contentSchema = z.object({
  language: z.string().min(1, 'Language is required'),
  name: z.string().min(1, 'Name is required for this language'),
  description: z.string().optional(), // Ingredients/Description
});

const createProductSchema = z.object({
  name: z.string().min(1),
  description: z.string().optional(),
  basePrice: z.coerce.number().min(0),
  isActive: z.boolean().default(true),
  isAvailable: z.boolean().default(true),
  preparationTimeMinutes: z.coerce.number().int().optional(),
  type: z.enum(productTypes),
  ingredients: z.string().optional(),
  allergens: z.array(z.string()).optional(),
  displayOrder: z.coerce.number().int().optional(),
  categoryIds: z.array(z.string()).optional(),
  primaryCategoryId: z.string().optional(),
  variations: z.array(variationSchema).optional(),
  content: z.array(contentSchema).optional().refine(items => {
    if (!items) return true;
    const languages = items.map(item => item.language);
    return new Set(languages).size === languages.length;
  }, { message: 'Each language can only be used once' }),
}).refine(data => !data.categoryIds || data.categoryIds.length === 0 || !!data.primaryCategoryId, {
  message: "Primary category is required when categories are selected",
  path: ["primaryCategoryId"],
});
type CreateProductFormValues = z.infer<typeof createProductSchema>;

// Component Props
interface Category { id: string; name: string; }
interface CreateProductModalProps {
  isOpen: boolean;
  onClose: () => void;
  onProductCreated: () => void;
  categoryId?: string | null;
}

const CreateProductModal: React.FC<CreateProductModalProps> = ({ isOpen, onClose, onProductCreated, categoryId }) => {
  const { t } = useTranslation();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);

  const {
    register, handleSubmit, control, formState: { errors },
    setError, reset, watch, setValue,
  } = useForm<CreateProductFormValues>({
    resolver: zodResolver(createProductSchema),
    defaultValues: {
      isActive: true, isAvailable: true,
      categoryIds: categoryId ? [categoryId] : [],
      primaryCategoryId: categoryId || '',
      variations: [], content: [], allergens: [],
    },
  });

  const { fields: variationFields, append: appendVariation, remove: removeVariation } = useFieldArray({ control, name: 'variations' });
  const { fields: contentFields, append: appendContent, remove: removeContent } = useFieldArray({ control, name: 'content' });

  const selectedCategoryIds = watch('categoryIds', categoryId ? [categoryId] : []);

  useEffect(() => {
    if (isOpen) {
      const fetchAllCategories = async () => {
        const response = await getCategories();
        if (response.success) setCategories(response.data.items);
      };
      fetchAllCategories();
    }
  }, [isOpen]);
  
  useEffect(() => {
    const primaryId = watch('primaryCategoryId');
    if (primaryId && !selectedCategoryIds?.includes(primaryId)) {
      setValue('primaryCategoryId', '');
    }
  }, [selectedCategoryIds, watch, setValue]);

  const onSubmit = async (data: CreateProductFormValues) => {
    setIsSubmitting(true);
    const formattedContent = data.content?.reduce((acc, curr) => {
      acc[curr.language] = { name: curr.name, description: curr.description || '' };
      return acc;
    }, {} as { [key: string]: { name: string; description: string } });

    const productData: CreateProductData = {
      ...data,
      ingredients: data.ingredients ? data.ingredients.split(',').map(s => s.trim()) : [],
      categoryIds: data.categoryIds || [],
      primaryCategoryId: data.primaryCategoryId || '',
      content: formattedContent,
    };

    try {
      const response = await createProduct(productData);
      if (response.success) {
        onProductCreated();
        onClose();
        reset();
      } else {
        setError('root', { message: response.message || 'Failed to create product' });
      }
    } catch (error) {
      setError('root', { message: 'An unexpected error occurred.' });
    } finally {
      setIsSubmitting(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div className={modalStyles.modalOverlay}>
      <div className={modalStyles.modalContent}>
        <div className={modalStyles.modalHeader}>
          <h2>{t('create_new_product')}</h2>
        </div>
        <form onSubmit={handleSubmit(onSubmit)}>
          {errors.root && <p className={modalStyles.errorMessage}>{errors.root.message}</p>}
          
          <div className={modalStyles.formGrid}>
            {/* --- LEFT COLUMN --- */}
            <div className={modalStyles.formColumn}>
              <div className={modalStyles.formGroup}>
                <label>{t('product_name')}</label>
                <input {...register('name')} />
                {errors.name && <p className={modalStyles.errorMessage}>{errors.name.message}</p>}
              </div>
              
              <div className={modalStyles.formGroup}>
                <label>{t('description')}</label>
                <textarea {...register('description')} rows={4} />
              </div>

              <div className={modalStyles.formGroup}>
                <label>{t('ingredients')}</label>
                <input {...register('ingredients')} placeholder={t('ingredients_placeholder')} />
              </div>
              
              <div className={modalStyles.formGroup}>
                <h3>{t('categories')} ({t('optional')})</h3>
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
                              field.onChange(e.target.checked ? [...selectedIds, cat.id] : selectedIds.filter(id => id !== cat.id));
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
                <select {...register('primaryCategoryId')} disabled={!selectedCategoryIds || selectedCategoryIds.length === 0}>
                  <option value="">{t('select_primary_category')}</option>
                  {categories.filter(cat => selectedCategoryIds?.includes(cat.id)).map(cat => <option key={cat.id} value={cat.id}>{cat.name}</option>)}
                </select>
                {errors.primaryCategoryId && <p className={modalStyles.errorMessage}>{errors.primaryCategoryId.message}</p>}
              </div>
            </div>

            {/* --- RIGHT COLUMN --- */}
            <div className={modalStyles.formColumn}>
              <div className={styles.grid}>
                <div className={modalStyles.formGroup}>
                  <label>{t('base_price')}</label>
                  <input type="number" step="0.01" {...register('basePrice')} />
                  {errors.basePrice && <p className={modalStyles.errorMessage}>{errors.basePrice.message}</p>}
                </div>
                <div className={modalStyles.formGroup}>
                  <label>{t('product_type')}</label>
                  <select {...register('type')}>
                    {productTypes.map(type => <option key={type} value={type}>{t(`product_type_${type}`)}</option>)}
                  </select>
                </div>
              </div>

              <div className={modalStyles.formGroup}>
                <h3>{t('allergens')} ({t('optional')})</h3>
                <Controller
                  name="allergens"
                  control={control}
                  render={({ field }) => (
                    <div className={modalStyles.chipGroup}>
                      {allergensList.map(allergen => (
                        <div key={allergen} className={modalStyles.chip}>
                          <input
                            type="checkbox"
                            id={`allergen-chip-${allergen}`}
                            value={allergen}
                            checked={field.value?.includes(allergen)}
                            onChange={e => {
                              const selected = field.value || [];
                              field.onChange(e.target.checked ? [...selected, allergen] : selected.filter(a => a !== allergen));
                            }}
                          />
                          <label htmlFor={`allergen-chip-${allergen}`}>{t(`allergen_${allergen}`)}</label>
                        </div>
                      ))}
                    </div>
                  )}
                />
              </div>
            </div>
          </div>
          
          {/* --- FULL WIDTH SECTIONS --- */}
          <div className={modalStyles.fullWidth}>
            <div className={modalStyles.formGroup}>
              <h3>{t('multilingual_content')}</h3>
              {errors.content && <p className={modalStyles.errorMessage}>{errors.content.message}</p>}
              {contentFields.map((field, index) => (
                <div key={field.id} className={modalStyles.contentItem}>
                  <button type="button" className={modalStyles.cancelButton} onClick={() => removeContent(index)}>{t('remove')}</button>
                  <div className={styles.gridItem}>
                    <select {...register(`content.${index}.language`)}><option value="">Language</option>{supportedLanguages.map(l=> <option key={l} value={l}>{t(`lang_${l}`)}</option>)}</select>
                    <input {...register(`content.${index}.name`)} placeholder={t('name_in_language')} />
                    <textarea {...register(`content.${index}.description`)} placeholder={t('ingredients_in_language')} />
                  </div>
                </div>
              ))}
              <button type="button" className={`${styles.adminButton} ${modalStyles.addSectionButton}`} onClick={() => appendContent({ language: 'en', name: '', description: '' })}>{t('add_language_translation')}</button>
            </div>

            <div className={modalStyles.formGroup}>
              <h3>{t('variations')} ({t('optional')})</h3>
              {variationFields.map((field, index) => (
                <div key={field.id} className={modalStyles.variationItem}>
                  <button type="button" className={modalStyles.cancelButton} onClick={() => removeVariation(index)}>{t('remove')}</button>
                  <div className={modalStyles.formGroup}><label>{t('variation_name')}</label><input {...register(`variations.${index}.name`)} /></div>
                  <div className={modalStyles.formGroup}><label>{t('variation_description')}</label><input {...register(`variations.${index}.description`)} /></div>
                  <div className={styles.grid}>
                    <div className={modalStyles.formGroup}><label>{t('price_modifier')}</label><input type="number" step="0.01" {...register(`variations.${index}.priceModifier`)} /></div>
                    <div className={modalStyles.formGroup}><label>{t('display_order')}</label><input type="number" {...register(`variations.${index}.displayOrder`)} /></div>
                  </div>
                  <div className={`${modalStyles.formGroup} ${modalStyles.chip}`}>
                    <input type="checkbox" id={`v-active-${index}`} {...register(`variations.${index}.isActive`)} defaultChecked={field.isActive} />
                    <label htmlFor={`v-active-${index}`}>{t('active')}</label>
                  </div>
                </div>
              ))}
              <button type="button" className={`${styles.adminButton} ${modalStyles.addSectionButton}`} onClick={() => appendVariation({ name: '', description: '', priceModifier: 0, isActive: true, displayOrder: 0 })}>{t('add_variation')}</button>
            </div>
          </div>
          
          <div className={modalStyles.buttonGroup}>
            <button type="submit" className={modalStyles.submitButton} disabled={isSubmitting}>{isSubmitting ? t('creating...') : t('create_product')}</button>
            <button type="button" onClick={onClose} className={modalStyles.cancelButton} disabled={isSubmitting}>{t('cancel')}</button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default CreateProductModal;
