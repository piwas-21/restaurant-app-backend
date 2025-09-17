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
const productTypes = ["mainItem", "sideItem", "beverage", "dessert", "sauce", "addOn"];
const supportedLanguages = ["en", "tr", "es", "ar", "de", "fr", "it"];

// Zod Schema
const variationSchema = z.object({
  id: z.string().optional(),
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

const editProductSchema = z.object({
  name: z.string().min(1),
  description: z.string().optional(),
  basePrice: z.coerce.number().min(0),
  isActive: z.boolean(),
  isAvailable: z.boolean(),
  preparationTimeMinutes: z.coerce.number().int().optional(),
  type: z.enum(productTypes),
  ingredients: z.string().optional(),
  allergens: z.array(z.string()).optional(),
  displayOrder: z.coerce.number().int().optional(),
  categoryIds: z.array(z.string()).optional(),
  primaryCategoryId: z.string().optional(),
  variations: z.array(variationSchema).optional(),
  content: z.array(contentSchema).optional(),
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

  const {
    register, handleSubmit, control, formState: { errors },
    setError, reset, watch,
  } = useForm<EditProductFormValues>({
    resolver: zodResolver(editProductSchema),
  });

  useEffect(() => {
    if (product) {
      const flattenedContent = product.content ? Object.entries(product.content).map(([lang, data]: [string, any]) => ({
        language: lang,
        name: data.name,
        description: data.description,
      })) : [];

      reset({
        ...product,
        ingredients: product.ingredients?.join(', '),
        categoryIds: product.categories?.map((c: any) => c.categoryId),
        primaryCategoryId: product.categories?.find((c: any) => c.isPrimary)?.categoryId,
        content: flattenedContent,
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
    const formattedContent = data.content?.reduce((acc, curr) => {
      acc[curr.language] = { name: curr.name, description: curr.description || '' };
      return acc;
    }, {} as { [key: string]: { name: string; description: string } });

    const productData = {
      ...data,
      id: product.id,
      ingredients: data.ingredients ? data.ingredients.split(',').map(s => s.trim()) : [],
      content: formattedContent,
    };

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
          <h2>{t('edit_product')}</h2>
        </div>
        <form onSubmit={handleSubmit(onSubmit)}>
          <div className={modalStyles.formGrid}>
            <div className={modalStyles.formColumn}>
              <div className={modalStyles.formGroup}><label>{t('product_name')}</label><input {...register('name')} /></div>
              <div className={modalStyles.formGroup}><label>{t('description')}</label><textarea {...register('description')} /></div>
              <div className={modalStyles.formGroup}><label>{t('ingredients')}</label><input {...register('ingredients')} /></div>
            </div>
            <div className={modalStyles.formColumn}>
              <div className={modalStyles.formGroup}><label>{t('base_price')}</label><input type="number" step="0.01" {...register('basePrice')} /></div>
              <div className={modalStyles.formGroup}>
                <label>{t('product_type')}</label>
                <select {...register('type')}>
                  {productTypes.map(type => <option key={type} value={type}>{t(`product_type_${type}`)}</option>)}
                </select>
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
                    <select {...register(`content.${index}.language`)}><option value="">Language</option>{supportedLanguages.map(l=> <option key={l} value={l}>{t(`lang_${l}`)}</option>)}</select>
                    <input {...register(`content.${index}.name`)} placeholder={t('name_in_language')} />
                    <textarea {...register(`content.${index}.description`)} placeholder={t('ingredients_in_language')} />
                  </div>
                  <button type="button" className={modalStyles.cancelButton} onClick={() => removeContent(index)}>{t('remove')}</button>
                </div>
              ))}
              <button type="button" className={`${styles.adminButton} ${modalStyles.addSectionButton}`} onClick={() => appendContent({ language: 'en', name: '', description: '' })}>{t('add_language_translation')}</button>
            </div>
          </div>

          <div className={modalStyles.buttonGroup}>
            <button type="submit" className={modalStyles.submitButton} disabled={isSubmitting}>{isSubmitting ? t('updating...') : t('update_product')}</button>
            <button type="button" onClick={onClose} className={modalStyles.cancelButton}>{t('cancel')}</button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default EditProductModal;
