'use client';

import React, { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useForm, useFieldArray } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useTranslation } from 'react-i18next';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import { getCategories } from '@/services/categoryService';

// Import separated components and utilities
import { CreateProductModalProps, productTypes } from './product/types';
import { createMenuBundleSchema, MenuBundleFormData } from './product/schemas';
import { MultilingualContent } from './product/MultilingualContent';
import { submitProductForm } from './product/productFormUtils';
import MenuScheduleEditor from '@/components/admin/menu-editor/MenuScheduleEditor';
import MenuSectionEditor from '@/components/admin/menu-editor/MenuSectionEditor';
import { MenuDefinition } from '@/types/menu';
import styles from '@/app/styles/AdminPage.module.css';

const CreateMenuBundleModal: React.FC<CreateProductModalProps> = ({
  isOpen,
  onClose,
  onProductCreated,
}) => {
  const { t, i18n } = useTranslation();
  const [submissionStatus, setSubmissionStatus] = useState<'idle' | 'creating' | 'uploading'>('idle');
  const [imageFiles, setImageFiles] = useState<File[]>([]);

  const {
    register,
    handleSubmit,
    control,
    formState: { errors },
    setError,
    reset,
    watch,
    setValue,
  } = useForm<MenuBundleFormData>({
    resolver: zodResolver(createMenuBundleSchema) as any,
    defaultValues: {
      name: '',
      basePrice: 0,
      type: 'menu' as const,
      isActive: true,
      isAvailable: true,
      isSpecial: false,
      content: [],
      preparationTimeMinutes: 0,
      displayOrder: 0,
    },
  });

  const [menuDefinition, setMenuDefinition] = useState<MenuDefinition>({
    id: '', // Will be assigned by backend
    isAlwaysAvailable: true,
    availableMonday: true,
    availableTuesday: true,
    availableWednesday: true,
    availableThursday: true,
    availableFriday: true,
    availableSaturday: true,
    availableSunday: true,
    sections: []
  });

  const { fields: contentFields, append: appendContent, remove: removeContent } = useFieldArray({
    control,
    name: 'content'
  });

  const currentLanguage = i18n.language || 'en';

  useEffect(() => {
    if (!isOpen) {
      reset();
      setImageFiles([]);
      setSubmissionStatus('idle');
      setMenuDefinition({
        id: '',
        isAlwaysAvailable: true,
        availableMonday: true,
        availableTuesday: true,
        availableWednesday: true,
        availableThursday: true,
        availableFriday: true,
        availableSaturday: true,
        availableSunday: true,
        sections: []
      });
    }
  }, [isOpen, reset]);

  // Sync menuDefinition state to form for validation
  useEffect(() => {
    setValue('menuDefinition', menuDefinition as any);
  }, [menuDefinition, setValue]);

  const onSubmit = async (data: MenuBundleFormData) => {
    // Ensure type is menu
    // data.type is already 'menu' from schema default/literal

    // Attach menu definition
    (data as any).menuDefinition = {
      ...menuDefinition,
      sections: menuDefinition.sections.map(s => ({
        ...s,
        items: s.items.map(i => ({
          productId: i.productId,
          additionalPrice: i.additionalPrice,
          displayOrder: i.displayOrder,
          isDefault: i.isDefault
        }))
      }))
    };

    await submitProductForm({
      data: data as any, // Cast to any to bypass strict FormData check in util
      imageFiles,
      currentLanguage,
      detailedIngredients: [], 
      setSubmissionStatus,
      setError: setError as any,
      onProductCreated,
      onClose,
      reset: reset as any,
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
          <h2>{t('create_new_menu_bundle')}</h2>
        </div>

        <form onSubmit={handleSubmit(onSubmit)}>
          {errors.root && <p className={modalStyles.errorMessage}>{errors.root.message}</p>}

          <div className={modalStyles.formGrid}>
            {/* Basic Info Column */}
            <div className={modalStyles.formColumn}>
              <div className={modalStyles.formGroup}>
                <label>{t('menu_bundle_name')}</label>
                <input {...register('name')} placeholder={t('enter_menu_bundle_name')} />
                {errors.name && <p className={modalStyles.errorMessage}>{errors.name.message}</p>}
              </div>

              <div className={modalStyles.formGroup}>
                <label>{t('description')}</label>
                <textarea {...register('description')} rows={4} />
              </div>
            </div>

            {/* Details Column */}
            <div className={modalStyles.formColumn}>
              <div className={styles.grid}>
                <div className={modalStyles.formGroup}>
                  <label>{t('base_price')}</label>
                  <input type="number" step="0.01" {...register('basePrice')} />
                  {errors.basePrice && <p className={modalStyles.errorMessage}>{errors.basePrice.message}</p>}
                </div>

                <div className={modalStyles.formGroup}>
                  <label>{t('preparation_time_minutes')}</label>
                  <input type="number" min="0" step="1" {...register('preparationTimeMinutes')} placeholder="0" />
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
                <label>
                  {t('menu_image')} ({t('optional')})
                </label>
                <input
                  type="file"
                  multiple
                  accept="image/*"
                  onChange={(e) => setImageFiles(Array.from(e.target.files || []))}
                />
                {imageFiles.length > 0 && <p>{t('files_selected', { count: imageFiles.length })}</p>}
              </div>
            </div>
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
            
            <div className={modalStyles.sectionDivider}></div>
            
            <h3>{t('menu_availability')}</h3>
            <MenuScheduleEditor
              menuDefinition={menuDefinition}
              onChange={setMenuDefinition}
            />

            <div className={modalStyles.sectionDivider}></div>

            <h3>{t('menu_sections')}</h3>
            <MenuSectionEditor
              sections={menuDefinition.sections}
              onChange={(newSections) => setMenuDefinition({ ...menuDefinition, sections: newSections })}
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
               t('create_menu_bundle')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );

  return createPortal(modalContent, document.body);
};

export default CreateMenuBundleModal;
