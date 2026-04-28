'use client';

import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useSnackbar } from 'notistack';
import { useTranslation } from 'react-i18next';
import { invalidateRestaurantInfoCache, useRestaurantInfo } from '@/hooks/useRestaurantInfo';
import { addPhoneNumber, deletePhoneNumber, updatePhoneNumber } from '@/services/restaurantInfoService';
import type { RestaurantPhoneNumberDto } from '@/types/restaurantInfo';
import {
  phoneNumberSchema,
  type PhoneNumberFormData,
  type PhoneNumberFormInput,
  type PhoneNumberFormOutput,
} from './schemas';
import ConfirmationModal from '@/components/common/ConfirmationModal';
import FormField from '@/components/design-system/FormField';
import StatusBadge from '@/components/design-system/StatusBadge';
import styles from './PhoneNumberManager.module.css';

interface Props {
  phones: RestaurantPhoneNumberDto[];
}

const emptyForm: PhoneNumberFormData = {
  label: null,
  number: '',
  whatsAppEnabled: false,
  displayOrder: 0,
  isActive: true,
};

export default function PhoneNumberManager({ phones }: Props) {
  const { t } = useTranslation();
  const { enqueueSnackbar } = useSnackbar();
  const { refetch } = useRestaurantInfo();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [isAdding, setIsAdding] = useState(false);
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<PhoneNumberFormInput, unknown, PhoneNumberFormOutput>({
    resolver: zodResolver(phoneNumberSchema),
    defaultValues: emptyForm,
  });

  const beginAdd = () => {
    setEditingId(null);
    setIsAdding(true);
    reset(emptyForm);
  };

  const beginEdit = (phone: RestaurantPhoneNumberDto) => {
    setIsAdding(false);
    setEditingId(phone.id);
    reset({
      label: phone.label,
      number: phone.number,
      whatsAppEnabled: phone.whatsAppEnabled,
      displayOrder: phone.displayOrder,
      isActive: phone.isActive,
    });
  };

  const cancel = () => {
    setEditingId(null);
    setIsAdding(false);
    reset(emptyForm);
  };

  const onSubmit = handleSubmit(async (data) => {
    try {
      const dto = {
        label: data.label || null,
        number: data.number,
        whatsAppEnabled: data.whatsAppEnabled,
        displayOrder: data.displayOrder,
        isActive: data.isActive,
      };
      const response = editingId
        ? await updatePhoneNumber(editingId, { id: editingId, ...dto })
        : await addPhoneNumber(dto);
      if (response.success) {
        invalidateRestaurantInfoCache();
        await refetch();
        enqueueSnackbar(t('phone_save_success', 'Phone saved'), { variant: 'success' });
        cancel();
      } else {
        enqueueSnackbar(response.message ?? t('phone_save_failed', 'Failed to save phone'), {
          variant: 'error',
        });
      }
    } catch {
      enqueueSnackbar(t('phone_save_failed', 'Failed to save phone'), { variant: 'error' });
    }
  });

  const onConfirmDelete = async () => {
    if (!pendingDeleteId) return;
    const id = pendingDeleteId;
    setPendingDeleteId(null);
    try {
      const response = await deletePhoneNumber(id);
      if (response.success) {
        invalidateRestaurantInfoCache();
        await refetch();
        enqueueSnackbar(t('phone_delete_success', 'Phone deleted'), { variant: 'success' });
      } else {
        enqueueSnackbar(response.message ?? t('phone_delete_failed', 'Failed to delete'), {
          variant: 'error',
        });
      }
    } catch {
      enqueueSnackbar(t('phone_delete_failed', 'Failed to delete'), { variant: 'error' });
    }
  };

  const isFormOpen = isAdding || editingId !== null;

  return (
    <fieldset className={styles.fieldset}>
      <legend>{t('phone_numbers', 'Phone numbers')}</legend>

      <ul className={styles.list}>
        {phones.length === 0 && <li className={styles.empty}>{t('phone_empty', 'No phone numbers yet')}</li>}
        {phones.map((p) => (
          <li key={p.id} className={styles.row}>
            <span className={styles.number}>{p.number}</span>
            {p.label && <span className={styles.label}>{p.label}</span>}
            {p.whatsAppEnabled && <StatusBadge tone="success">WhatsApp</StatusBadge>}
            {!p.isActive && <StatusBadge tone="neutral">{t('phone_inactive', 'Inactive')}</StatusBadge>}
            <div className={styles.rowActions}>
              <button type="button" onClick={() => beginEdit(p)}>
                {t('edit', 'Edit')}
              </button>
              <button type="button" className={styles.danger} onClick={() => setPendingDeleteId(p.id)}>
                {t('delete', 'Delete')}
              </button>
            </div>
          </li>
        ))}
      </ul>

      {!isFormOpen && (
        <button type="button" className={styles.addBtn} onClick={beginAdd}>
          {t('phone_add', 'Add phone number')}
        </button>
      )}

      {/* Inline form panel rather than a modal — phone CRUD happens in
          the flow of editing the parent settings, so a stacked dialog
          would obscure context. The list above and the form below share
          the same scroll position. */}
      {isFormOpen && (
        <div className={styles.formCard}>
          <FormField label={t('phone_number_label', 'Phone number (E.164)')} error={errors.number?.message}>
            <input type="tel" placeholder="+41227863333" {...register('number')} />
          </FormField>

          <FormField label={t('phone_label', 'Label (optional)')}>
            <input
              type="text"
              placeholder={t('phone_label_placeholder', 'Reservations, Reception...')}
              {...register('label')}
            />
          </FormField>

          <div className={styles.row}>
            <label className={styles.checkbox}>
              <input type="checkbox" {...register('whatsAppEnabled')} />
              <span>{t('phone_whatsapp', 'WhatsApp enabled')}</span>
            </label>
            <label className={styles.checkbox}>
              <input type="checkbox" {...register('isActive')} />
              <span>{t('phone_active', 'Active')}</span>
            </label>
            <FormField label={t('phone_display_order', 'Display order')}>
              <input type="number" min={0} {...register('displayOrder')} />
            </FormField>
          </div>

          <div className={styles.actions}>
            <button type="button" onClick={onSubmit} disabled={isSubmitting}>
              {isSubmitting ? t('saving', 'Saving...') : t('save', 'Save')}
            </button>
            <button type="button" onClick={cancel} disabled={isSubmitting}>
              {t('cancel', 'Cancel')}
            </button>
          </div>
        </div>
      )}
      <ConfirmationModal
        isOpen={pendingDeleteId !== null}
        onClose={() => setPendingDeleteId(null)}
        onConfirm={onConfirmDelete}
        message={t('phone_delete_confirm', 'Delete this phone number?')}
      />
    </fieldset>
  );
}
