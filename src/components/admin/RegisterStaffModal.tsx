import React, { useRef } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { staffRegistrationSchema } from '@/schemas/auth.schema';
import styles from '@/app/styles/RegisterStaffModal.module.css';
import { useTranslation } from 'react-i18next';
import { registerStaff } from '@/services/userService';
import { useRoleHelpers } from '@/hooks/useRoleHelpers';

type RegisterStaffFormValues = z.infer<typeof staffRegistrationSchema>;

interface RegisterStaffModalProps {
  isOpen: boolean;
  onClose: () => void;
  onStaffRegistered: () => void;
}

const RegisterStaffModal: React.FC<RegisterStaffModalProps> = ({ isOpen, onClose, onStaffRegistered }) => {
  const { t } = useTranslation();
  const { getRoleLabel, staffRoles } = useRoleHelpers();
  const modalContentRef = useRef<HTMLDivElement>(null);
  const {
    register,
    handleSubmit,
    formState: { errors },
    setError,
    reset,
  } = useForm<RegisterStaffFormValues>({
    resolver: zodResolver(staffRegistrationSchema),
  });

  const onSubmit = async (data: RegisterStaffFormValues) => {
    try {
        const response = await registerStaff(data) as { success: boolean; message?: string; errors?: string[] };
        if (response.success) {
            onStaffRegistered();
            onClose();
            reset(); // Reset form fields on successful registration
        } else {
            if (response.errors && Array.isArray(response.errors)) {
              response.errors.forEach((error: string) => {
                if (error.includes('password') || error.includes('Password')) {
                  setError('password', { message: error });
                } else if (error.includes('email') || error.includes('Email')) {
                  setError('email', { message: error });
                } else {
                  setError('root', { message: error });
                }
              });
            } else {
              setError('root', { message: response.message || 'Registration failed' });
            }
        }
    } catch {
        setError('root', { message: 'An unexpected error occurred.' });
    }
  };

  const handleOverlayClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (modalContentRef.current && !modalContentRef.current.contains(e.target as Node)) {
      onClose();
    }
  };

  if (!isOpen) {
    return null;
  }

  return (
    <div className={styles.modalOverlay} onClick={handleOverlayClick}>
      <div className={styles.modalContent} ref={modalContentRef}>
        <h2>{t('register_staff')}</h2>
        <form onSubmit={handleSubmit(onSubmit)}>
          {errors.root && <p className={styles.errorMessage}>{errors.root.message}</p>}
          <div className={styles.formGroup}>
            <label htmlFor="firstName">{t('first_name')}</label>
            <input id="firstName" {...register('firstName')} />
            {errors.firstName && <p className={styles.errorMessage}>{errors.firstName.message}</p>}
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="lastName">{t('last_name')}</label>
            <input id="lastName" {...register('lastName')} />
            {errors.lastName && <p className={styles.errorMessage}>{errors.lastName.message}</p>}
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="email">{t('email_label')}</label>
            <input id="email" type="email" {...register('email')} />
            {errors.email && <p className={styles.errorMessage}>{errors.email.message}</p>}
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="password">{t('password_label')}</label>
            <input id="password" type="password" {...register('password')} />
            {errors.password && <p className={styles.errorMessage}>{errors.password.message}</p>}
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="confirmPassword">{t('confirm_password_label')}</label>
            <input id="confirmPassword" type="password" {...register('confirmPassword')} />
            {errors.confirmPassword && <p className={styles.errorMessage}>{errors.confirmPassword.message}</p>}
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="role">{t('role')}</label>
            <select id="role" {...register('role')}>
              {staffRoles.map((role) => (
                <option key={role} value={role}>
                  {getRoleLabel(role)}
                </option>
              ))}
            </select>
            {errors.role && <p className={styles.errorMessage}>{errors.role.message}</p>}
          </div>
          <div className={styles.buttonGroup}>
            <button type="submit" className={styles.submitButton}>{t('register')}</button>
            <button type="button" onClick={onClose} className={styles.cancelButton}>{t('cancel')}</button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default RegisterStaffModal;
