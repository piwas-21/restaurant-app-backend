'use client';

import React, { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useForm, useFieldArray, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useTranslation } from 'react-i18next';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import styles from '@/app/styles/AdminPage.module.css';

// Import separated components and utilities
import { EditProductModalProps } from './product/types';
import { editMenuBundleSchema, EditMenuBundleFormData } from './product/schemas';
import { MultilingualContent } from './product/MultilingualContent';
import { submitEditProductForm } from './product/productFormUtils';
import MenuScheduleEditor from '@/components/admin/menu-editor/MenuScheduleEditor';
import MenuSectionEditor from '@/components/admin/menu-editor/MenuSectionEditor';
import { MenuDefinition } from '@/types/menu';

const EditMenuBundleModal: React.FC<EditProductModalProps> = ({
  isOpen,
  onClose,
  onProductUpdated,
  product
}) => {
  const { t, i18n } = useTranslation();
  const [isSubmitting, setIsSubmitting] = useState(false);
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
  } = useForm<EditMenuBundleFormData>({
    resolver: zodResolver(editMenuBundleSchema) as any,
    defaultValues: {
      name: '',
      description: '',
      basePrice: 0,
      isActive: true,
      isAvailable: true,
      isSpecial: false,
      type: 'menu',
      content: [],
      preparationTimeMinutes: 0,
      displayOrder: 0,
    }
  });

  const [menuDefinition, setMenuDefinition] = useState<MenuDefinition>({
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

  const { fields: contentFields, append: appendContent, remove: removeContent } = useFieldArray({
    control,
    name: 'content'
  });

  const currentLanguage = i18n.language || 'en';

  useEffect(() => {
    if (!isOpen) {
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

      reset({
        id: product.id,
        name: product.name || '',
        description: product.description || '',
        basePrice: product.basePrice || 0,
        isActive: product.isActive ?? true,
        isAvailable: product.isAvailable ?? true,
        isSpecial: product.isSpecial ?? false,
        type: 'menu',
        content: flattenedContent,
        preparationTimeMinutes: product.preparationTimeMinutes || 0,
        displayOrder: product.displayOrder || 0,
      });

      if (product.menuDefinition) {
        setMenuDefinition({
          id: product.menuDefinition.id || '',
          isAlwaysAvailable: product.menuDefinition.isAlwaysAvailable ?? true,
          startTime: product.menuDefinition.startTime,
          endTime: product.menuDefinition.endTime,
          availableMonday: product.menuDefinition.availableMonday ?? true,
          availableTuesday: product.menuDefinition.availableTuesday ?? true,
          availableWednesday: product.menuDefinition.availableWednesday ?? true,
          availableThursday: product.menuDefinition.availableThursday ?? true,
          availableFriday: product.menuDefinition.availableFriday ?? true,
          availableSaturday: product.menuDefinition.availableSaturday ?? true,
          availableSunday: product.menuDefinition.availableSunday ?? true,
          sections: product.menuDefinition.sections || []
        });
      }
    }
  }, [product, reset]);

  // Sync menuDefinition state to form for validation
  useEffect(() => {
    setValue('menuDefinition', menuDefinition as any);
  }, [menuDefinition, setValue]);

  const onSubmit = async (data: EditMenuBundleFormData) => {
    console.log("EditMenuBundleModal onSubmit called", data);
    try {
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

        console.log("Calling submitEditProductForm with", data);
        await submitEditProductForm({
        data: data as any, // Cast to any to bypass strict EditFormData check
        product,
        imageFiles,
        detailedIngredients: [],
        setIsSubmitting,
        setError: setError as any,
        onProductUpdated,
        onClose,
        });
    } catch (e) {
        console.error("Error in onSubmit:", e);
    }
  };

  console.log("Form Errors:", errors);

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
          <h2>{t('edit_menu_bundle')}</h2>
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
              type="submit"
              disabled={isSubmitting}
              className={modalStyles.submitButton}
            >
              {isSubmitting ? t('updating...') : t('update_menu_bundle')}
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
        </form>
      </div>
    </div>
  );

  return createPortal(modalContent, document.body);
};

export default EditMenuBundleModal;
