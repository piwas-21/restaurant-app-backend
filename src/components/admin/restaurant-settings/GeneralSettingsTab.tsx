'use client';

import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useSnackbar } from 'notistack';
import { useTranslation } from 'react-i18next';
import { useRestaurantInfo, invalidateRestaurantInfoCache } from '@/hooks/useRestaurantInfo';
import { updateRestaurantInfo } from '@/services/restaurantInfoService';
import FormField from '@/components/design-system/FormField';
import PhoneNumberManager from './PhoneNumberManager';
import { restaurantInfoSchema, type RestaurantInfoFormInput, type RestaurantInfoFormOutput } from './schemas';
import styles from './GeneralSettingsTab.module.css';

export default function GeneralSettingsTab() {
  const { t } = useTranslation();
  const { enqueueSnackbar } = useSnackbar();
  const { info, isLoading, error, refetch } = useRestaurantInfo();
  const [isSaving, setIsSaving] = useState(false);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isDirty },
  } = useForm<RestaurantInfoFormInput, unknown, RestaurantInfoFormOutput>({
    resolver: zodResolver(restaurantInfoSchema),
  });

  useEffect(() => {
    if (info) {
      reset({
        name: info.name,
        addressLine1: info.addressLine1,
        addressLine2: info.addressLine2 ?? null,
        city: info.city,
        postalCode: info.postalCode,
        country: info.country,
        latitude: info.latitude,
        longitude: info.longitude,
        email: info.email,
        website: info.website ?? null,
      });
    }
  }, [info, reset]);

  const onSubmit = handleSubmit(async (data) => {
    setIsSaving(true);
    try {
      const response = await updateRestaurantInfo({
        name: data.name,
        addressLine1: data.addressLine1,
        addressLine2: data.addressLine2 || null,
        city: data.city,
        postalCode: data.postalCode,
        country: data.country,
        latitude: data.latitude,
        longitude: data.longitude,
        email: data.email,
        website: data.website || null,
      });
      if (response.success) {
        invalidateRestaurantInfoCache();
        await refetch();
        enqueueSnackbar(t('general_settings_save_success', 'Settings saved'), { variant: 'success' });
      } else {
        enqueueSnackbar(response.message ?? t('general_settings_save_failed', 'Failed to save'), {
          variant: 'error',
        });
      }
    } catch {
      enqueueSnackbar(t('general_settings_save_failed', 'Failed to save'), { variant: 'error' });
    } finally {
      setIsSaving(false);
    }
  });

  if (isLoading && !info) {
    return <p>{t('loading', 'Loading...')}</p>;
  }
  if (error && !info) {
    return (
      <div className={styles.errorBanner} role="alert">
        <span>{t('general_settings_load_failed', 'Failed to load restaurant info')}</span>
        <button type="button" onClick={refetch}>
          {t('retry', 'Retry')}
        </button>
      </div>
    );
  }

  return (
    <form className={styles.form} onSubmit={onSubmit}>
      <fieldset className={styles.fieldset}>
        <legend>{t('general_settings_identity', 'Identity')}</legend>
        <FormField label={t('general_settings_name', 'Name')} error={errors.name?.message}>
          <input type="text" {...register('name')} />
        </FormField>
      </fieldset>

      <fieldset className={styles.fieldset}>
        <legend>{t('general_settings_address', 'Address')}</legend>
        <FormField label={t('general_settings_address_line1', 'Address line 1')} error={errors.addressLine1?.message}>
          <input type="text" {...register('addressLine1')} />
        </FormField>
        <FormField label={t('general_settings_address_line2', 'Address line 2')}>
          <input type="text" {...register('addressLine2')} />
        </FormField>
        <div className={styles.row}>
          <FormField label={t('general_settings_postal_code', 'Postal code')} error={errors.postalCode?.message}>
            <input type="text" {...register('postalCode')} />
          </FormField>
          <FormField label={t('general_settings_city', 'City')} error={errors.city?.message}>
            <input type="text" {...register('city')} />
          </FormField>
          <FormField label={t('general_settings_country', 'Country')} error={errors.country?.message}>
            <input type="text" {...register('country')} />
          </FormField>
        </div>
        <div className={styles.row}>
          <FormField label={t('general_settings_latitude', 'Latitude')} error={errors.latitude?.message}>
            <input type="number" step="any" {...register('latitude')} />
          </FormField>
          <FormField label={t('general_settings_longitude', 'Longitude')} error={errors.longitude?.message}>
            <input type="number" step="any" {...register('longitude')} />
          </FormField>
        </div>
      </fieldset>

      <fieldset className={styles.fieldset}>
        <legend>{t('general_settings_contact', 'Contact')}</legend>
        <FormField label={t('general_settings_email', 'Email')} error={errors.email?.message}>
          <input type="email" {...register('email')} />
        </FormField>
        <FormField label={t('general_settings_website', 'Website')} error={errors.website?.message}>
          <input type="url" placeholder="https://..." {...register('website')} />
        </FormField>
      </fieldset>

      <div className={styles.actions}>
        <button type="submit" disabled={isSaving || !isDirty}>
          {isSaving ? t('saving', 'Saving...') : t('save', 'Save')}
        </button>
      </div>

      {info && <PhoneNumberManager phones={info.phoneNumbers} />}
    </form>
  );
}
