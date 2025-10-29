'use client';

import React, { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useForm, useFieldArray } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useTranslation } from 'react-i18next';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import { getCategories } from '@/services/categoryService';

// Import separated components and utilities
import { CreateProductModalProps, Category } from './product/types';
import { createProductSchema, FormData } from './product/schemas';
import { ProductBasicInfo } from './product/ProductBasicInfo';
import { ProductDetails } from './product/ProductDetails';
import { MultilingualContent } from './product/MultilingualContent';
import { ProductVariations } from './product/ProductVariations';
import { SuggestedSideItemsPicker } from './product/SuggestedSideItemsPicker';
import { ProductIngredientsManager } from './product/ProductIngredientsManager';
import { submitProductForm } from './product/productFormUtils';

const CreateProductModal: React.FC<CreateProductModalProps> = ({
  isOpen,
  onClose,
  onProductCreated,
  categoryId
}) => {
  const { t, i18n } = useTranslation();
  const [submissionStatus, setSubmissionStatus] = useState<'idle' | 'creating' | 'uploading'>('idle');
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
      preparationTimeMinutes: 0,
      suggestedSideItemIds: [],
    },
  });

  const { fields: variationFields, append: appendVariation, remove: removeVariation } = useFieldArray({
    control,
    name: 'variations'
  });
  const { fields: contentFields, append: appendContent, remove: removeContent } = useFieldArray({
    control,
    name: 'content'
  });

  const selectedCategoryIds = watch('categoryIds', categoryId ? [categoryId] : []);
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
      setSelectedSideItemIds([]);
      setDetailedIngredients([]);
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
    await submitProductForm({
      data,
      imageFiles,
      currentLanguage,
      detailedIngredients,
      setSubmissionStatus,
      setError,
      onProductCreated,
      onClose,
      reset,
      setImageFiles
    });
  };

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
              errors={errors}
              control={control}
              selectedSideItemIds={selectedSideItemIds}
              onChange={(newIds) => {
                setSelectedSideItemIds(newIds);
                setValue('suggestedSideItemIds', newIds);
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
