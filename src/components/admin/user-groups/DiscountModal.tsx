import React, { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/RegisterStaffModal.module.css';
import { GroupDiscountDto, DiscountType } from '@/types/userGroupTypes';

const discountSchema = z.object({
  name: z.string().min(1, { message: 'Discount name is required' }),
  type: z.nativeEnum(DiscountType),
  value: z.coerce.number().min(0, { message: 'Value must be positive' }),
  minimumOrderAmount: z.coerce.number().min(0).optional(),
  maximumDiscountAmount: z.coerce.number().min(0).optional(),
  isActive: z.boolean()
});

type DiscountFormValues = z.infer<typeof discountSchema>;

interface DiscountModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (data: DiscountFormValues) => Promise<void>;
  initialData?: GroupDiscountDto | null;
  isSubmitting: boolean;
}

const DiscountModal: React.FC<DiscountModalProps> = ({
  isOpen,
  onClose,
  onSubmit,
  initialData,
  isSubmitting
}) => {
  const { t } = useTranslation();
  const isEditMode = !!initialData;

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset
  } = useForm<DiscountFormValues>({
    resolver: zodResolver(discountSchema),
    defaultValues: {
      type: DiscountType.Percentage,
      isActive: true,
      value: 0
    }
  });

  useEffect(() => {
    if (isOpen) {
      if (initialData) {
        reset({
          name: initialData.name,
          type: initialData.type,
          value: initialData.value,
          minimumOrderAmount: initialData.minimumOrderAmount,
          maximumDiscountAmount: initialData.maximumDiscountAmount,
          isActive: initialData.isActive
        });
      } else {
        reset({
          name: '',
          type: DiscountType.Percentage,
          value: 0,
          minimumOrderAmount: 0,
          maximumDiscountAmount: 0,
          isActive: true
        });
      }
    }
  }, [isOpen, initialData, reset]);

  const handleFormSubmit = async (data: DiscountFormValues) => {
    await onSubmit(data);
    if (!isSubmitting) {
      onClose();
    }
  };

  if (!isOpen) return null;

  return (
    <div className={styles.modalOverlay}>
      <div className={styles.modalContent}>
        <h2>{isEditMode ? t('edit_discount') : t('create_discount')}</h2>
        <form onSubmit={handleSubmit(handleFormSubmit)}>
          <div className={styles.formGroup}>
            <label htmlFor="name">{t('discount_name')} *</label>
            <input id="name" {...register('name')} />
            {errors.name && <p className={styles.errorMessage}>{errors.name.message}</p>}
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="type">{t('discount_type')}</label>
            <select id="type" {...register('type')}>
              <option value={DiscountType.Percentage}>{t('percentage')}</option>
              <option value={DiscountType.FixedAmount}>{t('fixed_amount')}</option>
            </select>
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="value">{t('discount_value')} *</label>
            <input type="number" step="0.01" id="value" {...register('value')} />
            {errors.value && <p className={styles.errorMessage}>{errors.value.message}</p>}
          </div>

          <div className={styles.row}>
            <div className={styles.formGroup}>
              <label htmlFor="minimumOrderAmount">{t('min_order_amount')}</label>
              <input type="number" step="0.01" id="minimumOrderAmount" {...register('minimumOrderAmount')} />
              {errors.minimumOrderAmount && <p className={styles.errorMessage}>{errors.minimumOrderAmount.message}</p>}
            </div>
            <div className={styles.formGroup}>
              <label htmlFor="maximumDiscountAmount">{t('max_discount_amount')}</label>
              <input type="number" step="0.01" id="maximumDiscountAmount" {...register('maximumDiscountAmount')} />
              {errors.maximumDiscountAmount && <p className={styles.errorMessage}>{errors.maximumDiscountAmount.message}</p>}
            </div>
          </div>

          <div className={`${styles.formGroup} ${styles.checkboxGroup}`}>
            <label htmlFor="isActive">
              <input type="checkbox" id="isActive" {...register('isActive')} />
              {t('active_status')}
            </label>
          </div>

          <div className={styles.buttonGroup}>
            <button type="submit" className={styles.submitButton} disabled={isSubmitting}>
              {isSubmitting ? t('saving...') : (isEditMode ? t('save_changes') : t('create'))}
            </button>
            <button type="button" onClick={onClose} className={styles.cancelButton} disabled={isSubmitting}>
              {t('cancel')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default DiscountModal;
