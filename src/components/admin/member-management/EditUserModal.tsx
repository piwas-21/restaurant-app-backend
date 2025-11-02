'use client';

import React, { useState, useEffect } from 'react';
import { ChevronDown, ChevronUp } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { UserDto, UserRole } from '@/types/user';
import styles from '@/app/styles/RegisterStaffModal.module.css';

interface EditUserModalProps {
  isOpen: boolean;
  user: UserDto | null;
  onClose: () => void;
  onSave: (updatedUser: Partial<UserDto>) => Promise<void>;
}

const EditUserModal: React.FC<EditUserModalProps> = ({ isOpen, user, onClose, onSave }) => {
  const { t } = useTranslation();
  const [formData, setFormData] = useState({
    firstName: '',
    lastName: '',
    email: '',
    phoneNumber: '',
    role: 'Server' as UserRole,
    changePassword: false,
    newPassword: '',
    confirmPassword: ''
  });
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    if (user) {
      setFormData({
        firstName: user.firstName || '',
        lastName: user.lastName || '',
        email: user.email || '',
        phoneNumber: user.phoneNumber || '',
        role: user.role,
        changePassword: false,
        newPassword: '',
        confirmPassword: ''
      });
      setErrors({});
    }
  }, [user]);

  if (!isOpen || !user) return null;

  const validateForm = (): boolean => {
    const newErrors: Record<string, string> = {};

    if (!formData.firstName.trim()) {
      newErrors.firstName = t('first_name_required', 'First name is required');
    }

    if (!formData.lastName.trim()) {
      newErrors.lastName = t('last_name_required', 'Last name is required');
    }

    if (!formData.email.trim()) {
      newErrors.email = t('email_required', 'Email is required');
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(formData.email)) {
      newErrors.email = t('email_invalid', 'Email is invalid');
    }

    if (formData.changePassword) {
      if (!formData.newPassword) {
        newErrors.newPassword = t('password_required', 'Password is required');
      } else if (formData.newPassword.length < 6) {
        newErrors.newPassword = t('password_min_length', 'Password must be at least 6 characters');
      }

      if (formData.newPassword !== formData.confirmPassword) {
        newErrors.confirmPassword = t('passwords_not_match', 'Passwords do not match');
      }
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!validateForm()) {
      return;
    }

    setIsSubmitting(true);
    try {
      const updateData: Partial<UserDto> & { password?: string } = {
        firstName: formData.firstName,
        lastName: formData.lastName,
        email: formData.email,
        phoneNumber: formData.phoneNumber || undefined,
        role: formData.role,
      };

      if (formData.changePassword && formData.newPassword) {
        updateData.password = formData.newPassword;
      }

      await onSave(updateData);
      onClose();
    } catch (error) {
      // eslint-disable-next-line no-console
      console.error('Error updating user:', error);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleChange = (field: string, value: string | boolean) => {
    setFormData(prev => ({ ...prev, [field]: value }));
    // Clear error when user starts typing
    if (errors[field]) {
      setErrors(prev => ({ ...prev, [field]: '' }));
    }
  };

  const handleOverlayClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const isStaffMember = ['Server', 'Cashier', 'KitchenStaff', 'Admin'].includes(user.role);

  return (
    <div className={styles.modalOverlay} onClick={handleOverlayClick}>
      <div className={styles.modalContent}>
        <h2>{t('edit_user', 'Edit User')}: {user.fullName}</h2>

        <form onSubmit={handleSubmit}>
          <div className={styles.formGroup}>
            <label htmlFor="firstName">{t('first_name', 'First Name')}</label>
            <input
              id="firstName"
              type="text"
              value={formData.firstName}
              onChange={(e) => handleChange('firstName', e.target.value)}
              placeholder={t('enter_first_name', 'Enter first name')}
            />
            {errors.firstName && <p className={styles.errorMessage}>{errors.firstName}</p>}
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="lastName">{t('last_name', 'Last Name')}</label>
            <input
              id="lastName"
              type="text"
              value={formData.lastName}
              onChange={(e) => handleChange('lastName', e.target.value)}
              placeholder={t('enter_last_name', 'Enter last name')}
            />
            {errors.lastName && <p className={styles.errorMessage}>{errors.lastName}</p>}
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="email">{t('email_label', 'Email')}</label>
            <input
              id="email"
              type="email"
              value={formData.email}
              onChange={(e) => handleChange('email', e.target.value)}
              placeholder={t('enter_email', 'Enter email')}
            />
            {errors.email && <p className={styles.errorMessage}>{errors.email}</p>}
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="phoneNumber">{t('phone_number', 'Phone Number')}</label>
            <input
              id="phoneNumber"
              type="tel"
              value={formData.phoneNumber}
              onChange={(e) => handleChange('phoneNumber', e.target.value)}
              placeholder={t('enter_phone', 'Enter phone number')}
            />
          </div>

          {isStaffMember && (
            <div className={styles.formGroup}>
              <label htmlFor="role">{t('role', 'Role')}</label>
              <select
                id="role"
                value={formData.role}
                onChange={(e) => handleChange('role', e.target.value as UserRole)}
              >
                <option value="Server">Server</option>
                <option value="Cashier">Cashier</option>
                <option value="KitchenStaff">Kitchen Staff</option>
                <option value="Admin">Admin</option>
              </select>
            </div>
          )}

          {/* Password Settings - Expandable Section */}
          <div style={{ marginTop: '1.5rem', paddingTop: '1.5rem', borderTop: '1px solid var(--border-color, #eee)' }}>
            <button
              type="button"
              onClick={() => handleChange('changePassword', !formData.changePassword)}
              style={{
                width: '100%',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                padding: '1rem',
                backgroundColor: 'transparent',
                border: '1px solid var(--border-color, #ddd)',
                borderRadius: '4px',
                cursor: 'pointer',
                fontSize: '1rem',
                fontWeight: '500',
                color: 'var(--text-color)',
                transition: 'all 0.2s ease'
              }}
              onMouseEnter={(e) => {
                e.currentTarget.style.backgroundColor = 'var(--background-color-hover, rgba(0,0,0,0.02))';
                e.currentTarget.style.borderColor = 'var(--primary-color, #c79063)';
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.backgroundColor = 'transparent';
                e.currentTarget.style.borderColor = 'var(--border-color, #ddd)';
              }}
            >
              <span>{t('change_password', 'Change Password')}</span>
              {formData.changePassword ? <ChevronUp size={20} /> : <ChevronDown size={20} />}
            </button>

            {!formData.changePassword && (
              <p style={{
                fontSize: '0.875rem',
                color: 'var(--text-secondary, #999)',
                marginTop: '0.5rem',
                marginLeft: '0.25rem'
              }}>
                {t('leave_password_unchanged', 'Current password will remain unchanged')}
              </p>
            )}
          </div>

          {formData.changePassword && (
            <div style={{
              marginTop: '1rem',
              padding: '1rem',
              backgroundColor: 'var(--background-color, rgba(0,0,0,0.02))',
              borderRadius: '4px',
              border: '1px solid var(--border-color, #eee)'
            }}>
              <div className={styles.formGroup} style={{ marginBottom: '1rem' }}>
                <label htmlFor="newPassword">{t('new_password', 'New Password')}</label>
                <input
                  id="newPassword"
                  type="password"
                  value={formData.newPassword}
                  onChange={(e) => handleChange('newPassword', e.target.value)}
                  placeholder={t('enter_new_password', 'Enter new password')}
                />
                {errors.newPassword && <p className={styles.errorMessage}>{errors.newPassword}</p>}
              </div>

              <div className={styles.formGroup} style={{ marginBottom: 0 }}>
                <label htmlFor="confirmPassword">{t('confirm_password', 'Confirm Password')}</label>
                <input
                  id="confirmPassword"
                  type="password"
                  value={formData.confirmPassword}
                  onChange={(e) => handleChange('confirmPassword', e.target.value)}
                  placeholder={t('confirm_new_password', 'Confirm new password')}
                />
                {errors.confirmPassword && <p className={styles.errorMessage}>{errors.confirmPassword}</p>}
              </div>
            </div>
          )}

          <div className={styles.buttonGroup}>
            <button
              type="submit"
              className={styles.submitButton}
              disabled={isSubmitting}
            >
              {isSubmitting ? t('saving', 'Saving...') : t('save_changes', 'Save Changes')}
            </button>
            <button
              type="button"
              onClick={onClose}
              className={styles.cancelButton}
              disabled={isSubmitting}
            >
              {t('cancel', 'Cancel')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default EditUserModal;
