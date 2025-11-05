'use client';

import React, { useState, useEffect } from 'react';
import { useForm, useFieldArray } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useTranslation } from 'react-i18next';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import { getCategories } from '@/services/categoryService';

// Import separated components and utilities
import { EditProductModalProps, Category } from './product/types';
import { editProductSchema, EditFormData } from './product/schemas';
import { ProductBasicInfo } from './product/ProductBasicInfo';
import { ProductDetails } from './product/ProductDetails';
import { MultilingualContent } from './product/MultilingualContent';
import { ProductVariations } from './product/ProductVariations';
import { SuggestedSideItemsPicker } from './product/SuggestedSideItemsPicker';
import { ProductIngredientsManager } from './product/ProductIngredientsManager';
import { submitEditProductForm } from './product/productFormUtils';

const EditProductModal: React.FC<EditProductModalProps> = ({
  isOpen,
  onClose,
  onProductUpdated,
  product
}) => {
  const { t, i18n } = useTranslation();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);
  const [imageFiles, setImageFiles] = useState<File[]>([]);
  const [selectedSideItemIds, setSelectedSideItemIds] = useState<string[]>([]);
  const [detailedIngredients, setDetailedIngredients] = useState<any[]>([]);

  const {
    register,
    handleSubmit,
    control,
    formState: { errors },
    setError,
    reset,
    watch,
    setValue,
  } = useForm<EditFormData>({
    resolver: zodResolver(editProductSchema) as any,
    defaultValues: {
      name: '',
      description: '',
      basePrice: 0,
      isActive: true,
      isAvailable: true,
      isSpecial: false,
      type: 'mainItem',
      allergens: [],
      variations: [],
      content: [],
      categoryIds: [],
      primaryCategoryId: '',
      preparationTimeMinutes: 0,
      suggestedSideItemIds: [],
    }
  });

  const { fields: variationFields, append: appendVariation, remove: removeVariation } = useFieldArray({
    control,
    name: 'variations'
  });
  const { fields: contentFields, append: appendContent, remove: removeContent } = useFieldArray({
    control,
    name: 'content'
  });

  const selectedCategoryIds = watch('categoryIds', []);
  const currentLanguage = i18n.language || 'en';

  useEffect(() => {
    if (isOpen) {
      const fetchAllCategories = async () => {
        const response = await getCategories() as { success: boolean; data?: { items: any[] } };
        if (response.success) setCategories(response.data?.items || []);
      };
      fetchAllCategories();
    } else {
      reset();
      setImageFiles([]);
      setIsSubmitting(false);
    }
  }, [isOpen, reset]);

  useEffect(() => {
    if (product) {
      const flattenedContent = product.content ? Object.entries(product.content).map(([lang, data]: [string, any]) => ({
        language: lang,
        name: data.name,
        description: data.description,
      })) : [];

      const safeCategoryIds = (product.categories?.map((c: any) => c.categoryId).filter((x: any) => !!x) || []) as string[];

      // Extract IDs from suggestedSideItems array
      const sideItemIds = Array.isArray(product.suggestedSideItems)
        ? product.suggestedSideItems.map((item: any) => item.id).filter((id: any) => !!id)
        : [];

      reset({
        name: product.name || '',
        description: product.description || '',
        basePrice: product.basePrice || 0,
        isActive: product.isActive ?? true,
        isAvailable: product.isAvailable ?? true,
        isSpecial: product.isSpecial ?? false,
        type: product.type || 'mainItem',
        allergens: Array.isArray(product.allergens) ? product.allergens : [],
        categoryIds: safeCategoryIds,
        primaryCategoryId: product.primaryCategoryId || (safeCategoryIds.length > 0 ? safeCategoryIds[0] : ''),
        variations: product.variations || [],
        content: flattenedContent,
        preparationTimeMinutes: product.preparationTimeMinutes || 0,
        displayOrder: product.displayOrder || 0,
        suggestedSideItemIds: sideItemIds,
      });

      setSelectedSideItemIds(sideItemIds);
      setDetailedIngredients(product.detailedIngredients || []);
    }
  }, [product, reset]);

  const onSubmit = async (data: EditFormData) => {
    await submitEditProductForm({
      data,
      product,
      imageFiles,
      detailedIngredients, // Pass ingredients to submit handler
      setIsSubmitting,
      setError,
      onProductUpdated,
      onClose,
    });
  };

  if (!isOpen) return null;

  const getErrorMessages = () => {
    const msgs: string[] = [];
    const visited = new WeakSet();
    const walk = (obj: any, path: string[] = []) => {
      if (!obj || typeof obj !== 'object') return;
      if (visited.has(obj)) return; // Prevent circular references
      visited.add(obj);

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
    <div
      className={modalStyles.modalOverlay}
      onClick={(e) => {
        // Close modal when clicking on the overlay (outside the modal content)
        if (e.target === e.currentTarget) {
          onClose();
        }
      }}
    >
      <div className={modalStyles.modalContent}>
        <div className={modalStyles.modalHeader}>
          <h2>{t('edit_product')}</h2>
        </div>

        <form onSubmit={handleSubmit(onSubmit)}>
          {errors.root && <p className={modalStyles.errorMessage}>{errors.root.message}</p>}

          <div className={modalStyles.formGrid}>
            <ProductBasicInfo
              register={register}
              errors={errors}
              categories={categories}
              selectedCategoryIds={selectedCategoryIds}
              control={control}
            />

            <ProductDetails
              register={register}
              errors={errors}
              control={control}
              imageFiles={imageFiles}
              setImageFiles={setImageFiles}
            />
          </div>

          <div className={modalStyles.fullWidth}>
            <MultilingualContent
              register={register}
              errors={errors}
              control={control}
              contentFields={contentFields}
              appendContent={appendContent}
              removeContent={removeContent}
              watch={watch}
              currentLanguage={currentLanguage}
            />

            <ProductVariations
              register={register}
              errors={errors}
              variationFields={variationFields}
              appendVariation={appendVariation}
              removeVariation={removeVariation}
            />

            <SuggestedSideItemsPicker
              control={control}
              errors={errors}
              selectedSideItemIds={selectedSideItemIds}
              onChange={(newSelection: string[]) => {
                setSelectedSideItemIds(newSelection);
                setValue('suggestedSideItemIds', newSelection);
              }}
            />

            <ProductIngredientsManager
              ingredients={detailedIngredients}
              onChange={setDetailedIngredients}
              productBasePrice={watch('basePrice') || 0}
            />
          </div>

          <div className={modalStyles.buttonGroup}>
            <button
              type="submit"
              disabled={isSubmitting}
              className={modalStyles.submitButton}
            >
              {isSubmitting ? t('updating...') : t('update_product')}
            </button>
            <button
              type="button"
              onClick={onClose}
              className={modalStyles.cancelButton}
              disabled={isSubmitting}
            >
              {t('cancel')}
            </button>
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
