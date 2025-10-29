import React from 'react';
import { Controller } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { ProductBasicInfoProps } from './types';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';

export const ProductBasicInfo: React.FC<ProductBasicInfoProps> = ({
  register,
  errors,
  categories,
  selectedCategoryIds,
  control
}) => {
  const { t } = useTranslation();

  return (
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
  );
};
