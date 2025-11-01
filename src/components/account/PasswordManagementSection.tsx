"use client";

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from './PasswordManagementSection.module.css';

interface PasswordManagementSectionProps {
  currentPassword: string;
  newPassword: string;
  confirmNewPassword: string;
  passwordErrors: Partial<Record<string, string>>;
  passwordSuccess: string;
  passwordStrength: number;
  passwordStrengthText: string;
  handleCurrentPasswordChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  handleNewPasswordChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  handleConfirmNewPasswordChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  handlePasswordChangeSubmit: (e: React.FormEvent) => Promise<void>;
  getStrengthBarStyle: (level: number) => string;
}

export default function PasswordManagementSection({
  currentPassword,
  newPassword,
  confirmNewPassword,
  passwordErrors,
  passwordSuccess,
  passwordStrengthText,
  handleCurrentPasswordChange,
  handleNewPasswordChange,
  handleConfirmNewPasswordChange,
  handlePasswordChangeSubmit,
  getStrengthBarStyle
}: PasswordManagementSectionProps) {
  const { t } = useTranslation();

  return (
    <section className={styles.section}>
      <h2 className={styles.sectionTitle}>{t('password_management_title', 'Password Management')}</h2>
      {passwordSuccess && <p className={styles.successMessage}>{passwordSuccess}</p>}
      {passwordErrors.form && <p className={styles.errorMessage}>{passwordErrors.form}</p>}
      <form onSubmit={handlePasswordChangeSubmit} noValidate>
        <div className={styles.formGroup}>
          <label htmlFor="currentPassword">{t('current_password_label', 'Current Password')}</label>
          <input type="password" id="currentPassword" name="currentPassword" value={currentPassword} onChange={handleCurrentPasswordChange} className={styles.formInput} />
          {passwordErrors.currentPassword && <p className={styles.errorMessage}>{passwordErrors.currentPassword}</p>}
        </div>

        <div className={styles.formGroup}>
          <label htmlFor="newPassword">{t('new_password_label', 'New Password')}</label>
          <input type="password" id="newPassword" name="newPassword" value={newPassword} onChange={handleNewPasswordChange} className={styles.formInput} />
          {newPassword && (
            <div className={styles.passwordStrengthContainer}>
              <span style={{marginRight: '0.5rem'}}>{t('password_strength_label', 'Password Strength:')}</span>
              {[1, 2, 3, 4, 5].map(level => (
                  <div key={level} className={`${styles.strengthBar} ${getStrengthBarStyle(level)}`} />
              ))}
              <span style={{marginLeft: '0.5rem'}}>{passwordStrengthText}</span>
            </div>
          )}
          {passwordErrors.newPassword && <p className={styles.errorMessage}>{passwordErrors.newPassword}</p>}
        </div>

        <div className={styles.formGroup}>
          <label htmlFor="confirmNewPassword">{t('confirm_new_password_label', 'Confirm New Password')}</label>
          <input type="password" id="confirmNewPassword" name="confirmNewPassword" value={confirmNewPassword} onChange={handleConfirmNewPasswordChange} className={styles.formInput} />
          {passwordErrors.confirmNewPassword && <p className={styles.errorMessage}>{passwordErrors.confirmNewPassword}</p>}
        </div>
        <div className={styles.formActions}>
          <button type="submit" className={styles.changePasswordButton}>{t('change_password_button', 'Change Password')}</button>
        </div>
      </form>
    </section>
  );
}
