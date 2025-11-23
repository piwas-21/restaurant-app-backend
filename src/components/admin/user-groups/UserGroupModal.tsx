import React, { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/RegisterStaffModal.module.css';
import modalStyles from './UserGroupModal.module.css';
import { UserGroupDto, DiscountType } from '@/types/userGroupTypes';

const userGroupSchema = z.object({
  name: z.string().min(1, { message: 'Group name is required' }),
  description: z.string().optional(),
  validFrom: z.string().optional(),
  validUntil: z.string().optional(),
  isActive: z.boolean(),
  // Initial discount fields (only used for creation)
  hasInitialDiscount: z.boolean().optional(),
  discountName: z.string().optional(),
  discountType: z.nativeEnum(DiscountType).optional(),
  discountValue: z.coerce.number().min(0).optional(),
  minOrderAmount: z.coerce.number().min(0).optional(),
  maxDiscountAmount: z.coerce.number().min(0).optional(),
}).refine((data) => {
  if (data.hasInitialDiscount) {
    return !!data.discountName && data.discountValue !== undefined && data.discountValue > 0;
  }
  return true;
}, {
  message: "Discount name and value are required when adding an initial discount",
  path: ["discountName"],
});

type UserGroupFormValues = z.infer<typeof userGroupSchema>;

interface UserGroupModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (data: UserGroupFormValues) => Promise<void>;
  initialData?: UserGroupDto | null;
  isSubmitting: boolean;
}

const UserGroupModal: React.FC<UserGroupModalProps> = ({
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
    reset,
    watch,
    setValue
  } = useForm<UserGroupFormValues>({
    resolver: zodResolver(userGroupSchema),
    defaultValues: {
      isActive: true,
      hasInitialDiscount: false,
      discountType: DiscountType.Percentage
    }
  });

  const hasInitialDiscount = watch('hasInitialDiscount');

  useEffect(() => {
    if (isOpen) {
      if (initialData) {
        reset({
          name: initialData.name,
          description: initialData.description,
          validFrom: initialData.validFrom ? new Date(initialData.validFrom).toISOString().split('T')[0] : '',
          validUntil: initialData.validUntil ? new Date(initialData.validUntil).toISOString().split('T')[0] : '',
          isActive: initialData.isActive,
          hasInitialDiscount: false // Can't add initial discount on edit
        });
      } else {
        reset({
          name: '',
          description: '',
          validFrom: '',
          validUntil: '',
          isActive: true,
          hasInitialDiscount: false,
          discountType: DiscountType.Percentage,
          discountValue: 0
        });
      }
    }
  }, [isOpen, initialData, reset]);

  const handleFormSubmit = async (data: UserGroupFormValues) => {
    await onSubmit(data);
    if (!isSubmitting) {
      onClose();
    }
  };

  if (!isOpen) return null;

  return (
    <div className={styles.modalOverlay}>
      <div className={styles.modalContent}>
        <h2>{isEditMode ? t('edit_user_group') : t('create_user_group')}</h2>
        <form onSubmit={handleSubmit(handleFormSubmit)}>
          <div className={styles.formGroup}>
            <label htmlFor="name">{t('group_name')} *</label>
            <input id="name" {...register('name')} />
            {errors.name && <p className={styles.errorMessage}>{errors.name.message}</p>}
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="description">{t('group_description')}</label>
            <textarea id="description" {...register('description')} />
            {errors.description && <p className={styles.errorMessage}>{errors.description.message}</p>}
          </div>

          <div className={styles.row}>
            <div className={styles.formGroup}>
              <label htmlFor="validFrom">{t('valid_from')}</label>
              <input type="date" id="validFrom" {...register('validFrom')} />
            </div>
            <div className={styles.formGroup}>
              <label htmlFor="validUntil">{t('valid_until')}</label>
              <input type="date" id="validUntil" {...register('validUntil')} />
            </div>
          </div>

          <div className={styles.formGroup}>
            <label>{t('status')}</label>
            <div className={styles.chipGroup}>
              <div className={styles.chip}>
                <input type="checkbox" id="group-active" {...register('isActive')} />
                <label htmlFor="group-active">{t('active')}</label>
              </div>
            </div>
          </div>

          {!isEditMode && (
            <>
              <div className={modalStyles.sectionHeader}>
                <h3 className={modalStyles.sectionTitle}>{t('initial_discount')} <span className={modalStyles.optionalText}>{t('optional')}</span></h3>
                {!hasInitialDiscount && (
                  <button
                    type="button"
                    className={styles.addButton}
                    onClick={() => setValue('hasInitialDiscount', true)}
                  >
                    + {t('add')}
                  </button>
                )}
              </div>

              {hasInitialDiscount && (
                <div className={`${styles.nestedForm} ${modalStyles.discountFormContainer}`}>
                  <button
                    type="button"
                    className={modalStyles.removeDiscountButton}
                    onClick={() => {
                      setValue('hasInitialDiscount', false);
                      setValue('discountName', '');
                      setValue('discountValue', 0);
                    }}
                  >
                    ×
                  </button>
                  
                  <div className={styles.formGroup}>
                    <label htmlFor="discountName">{t('discount_name')} *</label>
                    <input id="discountName" {...register('discountName')} />
                    {errors.discountName && <p className={styles.errorMessage}>{errors.discountName.message}</p>}
                  </div>

                  <div className={styles.formGroup}>
                    <label htmlFor="discountType">{t('discount_type')}</label>
                    <select id="discountType" {...register('discountType')}>
                      <option value={DiscountType.Percentage}>{t('percentage')}</option>
                      <option value={DiscountType.FixedAmount}>{t('fixed_amount')}</option>
                    </select>
                  </div>

                  <div className={styles.formGroup}>
                    <label htmlFor="discountValue">{t('discount_value')} *</label>
                    <input type="number" step="0.01" id="discountValue" {...register('discountValue')} />
                    {errors.discountValue && <p className={styles.errorMessage}>{errors.discountValue.message}</p>}
                  </div>

                  <div className={styles.row}>
                    <div className={styles.formGroup}>
                      <label htmlFor="minOrderAmount">{t('min_order_amount')}</label>
                      <input type="number" step="0.01" id="minOrderAmount" {...register('minOrderAmount')} />
                    </div>
                    <div className={styles.formGroup}>
                      <label htmlFor="maxDiscountAmount">{t('max_discount_amount')}</label>
                      <input type="number" step="0.01" id="maxDiscountAmount" {...register('maxDiscountAmount')} />
                    </div>
                  </div>
                </div>
              )}
            </>
          )}

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

export default UserGroupModal;
