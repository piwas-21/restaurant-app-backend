import React from 'react';
import { Controller } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { ProductDetailsProps, productTypes, allergensList } from './types';
import styles from '@/app/styles/AdminPage.module.css';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';

export const ProductDetails: React.FC<ProductDetailsProps> = ({
  register,
  errors,
  control,
  imageFiles,
  setImageFiles
}) => {
  const { t } = useTranslation();

  return (
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

        <div className={modalStyles.formGroup}>
          <label>{t('preparation_time_minutes')}</label>
          <input
            type="number"
            min="0"
            step="1"
            {...register('preparationTimeMinutes')}
            placeholder="0"
          />
          {errors.preparationTimeMinutes && (
            <p className={modalStyles.errorMessage}>{errors.preparationTimeMinutes.message}</p>
          )}
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
                      field.onChange(e.target.checked ? [...selected, allergen] : selected.filter((a: string) => a !== allergen));
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
  );
};
