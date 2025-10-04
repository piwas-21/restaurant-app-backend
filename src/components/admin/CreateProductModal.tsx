'use client';

import React, { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useForm, useFieldArray, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/AdminPage.module.css';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import { createProduct } from '@/services/menuService';
import { uploadBulkProductImages } from '@/services/productService';
import { getCategories } from '@/services/categoryService';
// No types needed from menu as we use local types

// Constants
const productTypes = ['mainItem', 'sideItem', 'beverage', 'dessert', 'sauce', 'addOn'] as const;
const allergensList = ['halal', 'vegan', 'vegetarian', 'gluten-free', 'contains_dairy', 'contains_nuts'] as const;
const supportedLanguages = ['en', 'tr', 'es', 'ar', 'de', 'fr', 'it'] as const;

// Zod Schemas
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
  description: z.string().optional(),
});

const createProductSchema = z.object({
  name: z.string().min(1),
  description: z.string().optional(),
  basePrice: z.coerce.number().min(0),
  isActive: z.boolean().default(true),
  isAvailable: z.boolean().default(true),
  isSpecial: z.boolean().default(false),
  type: z.enum(productTypes),
  ingredients: z.string().optional(),
  allergens: z.array(z.string()).optional(),
  categoryIds: z.array(z.string()).min(1, 'Select at least one category'),
  primaryCategoryId: z.string().min(1, 'Primary category is required'),
  variations: z.array(variationSchema).default([]),
  content: z.array(contentSchema).default([]).refine(items => {
    if (!items) return true;
    const languages = items.map(item => item.language);
    return new Set(languages).size === languages.length;
  }, { message: 'Each language can only be used once' }),
});

type FormData = z.infer<typeof createProductSchema>;

interface Category {
  id: string;
  name: string;
}

interface Props {
  isOpen: boolean;
  onClose: () => void;
  onProductCreated: () => void;
  categoryId?: string | null;
}

const CreateProductModal: React.FC<Props> = ({ isOpen, onClose, onProductCreated, categoryId }) => {
  const { t, i18n } = useTranslation();
  const [submissionStatus, setSubmissionStatus] = useState<'idle' | 'creating' | 'uploading'>('idle');
  const [categories, setCategories] = useState<Category[]>([]);
  const [imageFiles, setImageFiles] = useState<File[]>([]);
  console.log("categoryId:", categoryId);

  const {
    register,
    handleSubmit,
    control,
    formState: { errors },
    setError,
    reset,
    watch,
    setValue,
  } = useForm<FormData>({
    resolver: zodResolver(createProductSchema) as any,
    defaultValues: {
      name: '',
      basePrice: 0,
      type: 'mainItem' as const,
      isActive: true,
      isAvailable: true,
      isSpecial: false,
      categoryIds: categoryId ? [categoryId] : [],
      primaryCategoryId: categoryId || '',
      variations: [],
      content: [],
      allergens: [],
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
    } else {
      reset();
      setImageFiles([]);
      setSubmissionStatus('idle');
    }
  }, [isOpen, reset]);

  useEffect(() => {
    const primaryId = watch('primaryCategoryId');
    if (primaryId && !selectedCategoryIds?.includes(primaryId)) {
      setValue('primaryCategoryId', '');
    }
  }, [selectedCategoryIds, watch, setValue]);

  const onSubmit = async (data: FormData) => {
    setSubmissionStatus('creating');
    try {
      // Format content for the API
      const content: { [key: string]: { name: string; description: string } } = {};

      // Automatically add the main product data to content using the current user language
      const currentLanguage = i18n.language || 'en'; // Get current language from i18n
      content[currentLanguage] = {
        name: data.name,
        description: data.ingredients || ''
      };

      // Add any additional multilingual content
      data.content?.forEach(item => {
        if (item.language && item.language !== currentLanguage) {
          content[item.language] = {
            name: item.name,
            description: item.description || ''
          };
        }
      });

      // Format the product data
      const productData = {
        ...data,
        ingredients: data.ingredients ? data.ingredients.split(',').map(s => s.trim()) : [],
        content,
        variations: data.variations || []
      };

      const productResponse = await createProduct(productData);
      if (productResponse.success && productResponse.data.id) {
        if (imageFiles.length > 0) {
          setSubmissionStatus('uploading');
          const imageResponse = await uploadBulkProductImages(productResponse.data.id, imageFiles);
          if (!imageResponse.success) {
            // eslint-disable-next-line no-console
            console.error("Image upload failed:", imageResponse.message);
          }
        }
        onProductCreated();
        onClose();
        reset();
        setImageFiles([]);
      } else {
        setError('root', { message: productResponse.message || 'Failed to create product' });
      }
    } catch {
      setError('root', { message: 'An unexpected error occurred.' });
    } finally {
      setSubmissionStatus('idle');
    }
  };

  if (!isOpen) return null;

  console.log("Rendering CreateProductModal with isOpen:", isOpen);

  if (!isOpen) return null;

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const modalContent = (
    <div className={modalStyles.modalOverlay} onClick={handleBackdropClick}>
      <div className={modalStyles.modalContent}>
        <div className={modalStyles.modalHeader}>
          <h2>{t('create_new_product')}</h2>
        </div>

        <form onSubmit={handleSubmit(onSubmit as any)}>
          {errors.root && <p className={modalStyles.errorMessage}>{errors.root.message}</p>}

          <div className={modalStyles.formGrid}>
            {/* Left Column */}
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
                              field.onChange(e.target.checked ? [...selectedIds, cat.id] : selectedIds.filter(id => id !== cat.id));
                            }}
                          />
                          <label htmlFor={`category-chip-${cat.id}`}>{cat.name}</label>
                        </div>
                      ))}
                    </div>
                  )}
                />
                {errors.categoryIds && <p className={modalStyles.errorMessage}>{errors.categoryIds.message}</p>}
              </div>

              <div className={modalStyles.formGroup}>
                <label>{t('primary_category')}</label>
                <select {...register('primaryCategoryId')} disabled={!selectedCategoryIds || selectedCategoryIds.length === 0}>
                  <option value="">{t('select_primary_category')}</option>
                  {categories.filter(cat => selectedCategoryIds?.includes(cat.id)).map(cat => (
                    <option key={cat.id} value={cat.id}>{cat.name}</option>
                  ))}
                </select>
                {errors.primaryCategoryId && (
                  <p className={modalStyles.errorMessage}>{errors.primaryCategoryId.message}</p>
                )}
              </div>
            </div>

            {/* Right Column */}
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
                    {productTypes.map(type => (
                      <option key={type} value={type}>{t(`product_type_${type}`)}</option>
                    ))}
                  </select>
                </div>

                <div className={modalStyles.chipGroup}>
                  <div className={modalStyles.chip}>
                    <input type="checkbox" id="product-active" {...register('isActive')} />
                    <label htmlFor="product-active">{t('active')}</label>
                  </div>
                  <div className={modalStyles.chip}>
                    <input type="checkbox" id="product-available" {...register('isAvailable')} />
                    <label htmlFor="product-available">{t('available')}</label>
                  </div>
                  <div className={modalStyles.chip}>
                    <input type="checkbox" id="product-special" {...register('isSpecial')} />
                    <label htmlFor="product-special">{t('special_of_the_day_title')}</label>
                  </div>
                </div>
              </div>

              <div className={modalStyles.formGroup}>
                <label>{t('product_images')} ({t('optional')})</label>
                <input
                  type="file"
                  multiple
                  accept="image/*"
                  onChange={(e) => setImageFiles(Array.from(e.target.files || []))}
                />
                {imageFiles.length > 0 && (
                  <p>{t('files_selected', { count: imageFiles.length })}</p>
                )}
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

          {/* Full Width Sections */}
          <div className={modalStyles.fullWidth}>
            <div className={modalStyles.formGroup}>
              <h3>{t('multilingual_content')}</h3>
              {errors.content && <p className={modalStyles.errorMessage}>{errors.content.message}</p>}
              {contentFields.map((field, index) => (
                <div key={field.id} className={modalStyles.contentItem}>
                  <button
                    type="button"
                    className={modalStyles.cancelButton}
                    onClick={() => removeContent(index)}
                  >
                    {t('remove')}
                  </button>
                  <div className={styles.gridItem}>
                    {(() => {
                      const allContentItems = (watch('content') || []) as any[];
                      const usedLanguages = allContentItems.map(item => item.language).filter(Boolean);
                      const currentItemLanguage = (watch(`content.${index}.language`) as any) || '';
                      const currentMainLanguage = i18n.language || 'en'; // Get current language from i18n

                      return (
                        <select {...register(`content.${index}.language`)}>
                          <option value="">{t('select_language')}</option>
                          {supportedLanguages.map(lang => {
                            const isUsedByMain = lang === currentMainLanguage;
                            const isUsedByOther = usedLanguages.includes(lang) && lang !== currentItemLanguage;
                            const isDisabled = isUsedByMain || isUsedByOther;

                            return (
                              <option
                                key={lang}
                                value={lang}
                                disabled={isDisabled}
                              >
                                {t(`lang_${lang}`)} {isUsedByMain ? '(Auto-added)' : ''}
                              </option>
                            );
                          })}
                        </select>
                      );
                    })()}
                    <input {...register(`content.${index}.name`)} placeholder={t('name_in_language')} />
                    <textarea {...register(`content.${index}.description`)} placeholder={t('ingredients_in_language')} />
                  </div>
                </div>
              ))}
              <button
                type="button"
                className={`${styles.adminButton} ${modalStyles.addSectionButton}`}
                onClick={() => {
                  const allContentItems = (watch('content') || []) as any[];
                  const usedLanguages = allContentItems.map(item => item.language).filter(Boolean);
                  const currentMainLanguage = i18n.language || 'en'; // Get current language from i18n
                  const unavailableLanguages = [...usedLanguages, currentMainLanguage];
                  const nextAvailableLanguage = supportedLanguages.find(lang => !unavailableLanguages.includes(lang)) || '';

                  appendContent({ language: nextAvailableLanguage, name: '', description: '' });
                }}
              >
                {t('add_language_translation')}
              </button>
            </div>

            <div className={modalStyles.formGroup}>
              <h3>{t('variations')} ({t('optional')})</h3>
              {variationFields.map((field, index) => (
                <div key={field.id} className={modalStyles.variationItem}>
                  <button
                    type="button"
                    className={modalStyles.cancelButton}
                    onClick={() => removeVariation(index)}
                  >
                    {t('remove')}
                  </button>
                  <div className={modalStyles.formGroup}>
                    <label>{t('variation_name')}</label>
                    <input {...register(`variations.${index}.name`)} />
                  </div>
                  <div className={modalStyles.formGroup}>
                    <label>{t('variation_description')}</label>
                    <input {...register(`variations.${index}.description`)} />
                  </div>
                  <div className={modalStyles.formGroup}>
                    <label>{t('price_modifier')}</label>
                    <input
                      type="number"
                      step="0.01"
                      {...register(`variations.${index}.priceModifier`)}
                    />
                  </div>
                  <div className={modalStyles.formGroup}>
                    <label>{t('display_order')}</label>
                    <input
                      type="number"
                      {...register(`variations.${index}.displayOrder`)}
                    />
                  </div>
                  <div className={modalStyles.chipGroup}>
                    <div className={modalStyles.chip}>
                      <input
                        type="checkbox"
                        id={`variation-active-${index}`}
                        {...register(`variations.${index}.isActive`)}
                      />
                      <label htmlFor={`variation-active-${index}`}>{t('active')}</label>
                    </div>
                  </div>
                </div>
              ))}
              <button
                type="button"
                className={`${styles.adminButton} ${modalStyles.addSectionButton}`}
                onClick={() => appendVariation({
                  name: '',
                  description: '',
                  priceModifier: 0,
                  displayOrder: variationFields.length,
                  isActive: true
                })}
              >
                {t('add_variation')}
              </button>
            </div>
          </div>

          <div className={modalStyles.buttonGroup}>
            <button
              type="button"
              onClick={onClose}
              className={modalStyles.cancelButton}
              disabled={submissionStatus !== 'idle'}
            >
              {t('cancel')}
            </button>
            <button
              type="submit"
              disabled={submissionStatus !== 'idle'}
              className={modalStyles.submitButton}
            >
              {submissionStatus === 'creating' ? t('creating...') :
               submissionStatus === 'uploading' ? t('uploading...') :
               t('create_product')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );

  return createPortal(modalContent, document.body);
};

export default CreateProductModal;
