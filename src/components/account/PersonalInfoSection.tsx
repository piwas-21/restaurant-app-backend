"use client";

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from './PersonalInfoSection.module.css';
// Import the specific error keys type from the main page
import type { UserProfile, ProfileErrorKeys } from '../../app/account/page';

interface PersonalInfoSectionProps {
  profile: UserProfile;
  // Use the imported ProfileErrorKeys for profileErrors prop
  profileErrors: Partial<Record<ProfileErrorKeys, string>>;
  profileSuccess: string;
  handleProfileChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => void;
  handleProfileSave: (e: React.FormEvent) => Promise<void>;
}

export default function PersonalInfoSection({
    profile,
    profileErrors,
    profileSuccess,
    handleProfileChange,
    handleProfileSave
}: PersonalInfoSectionProps) {
  const { t } = useTranslation();

  return (
    <section className={styles.section}>
      <h2 className={styles.sectionTitle}>{t('personal_information_title', 'Personal Information')}</h2>
      {profileSuccess && <p className={styles.successMessage}>{profileSuccess}</p>}
      {profileErrors.form && <p className={styles.errorMessage}>{profileErrors.form}</p>}
      <form onSubmit={handleProfileSave} noValidate>
        <div className={styles.formGroup}>
          <label htmlFor="fullName">{t('full_name_label', 'Full Name')}</label>
          <input type="text" id="fullName" name="fullName" value={profile.fullName} onChange={handleProfileChange} className={styles.formInput} />
          {profileErrors.fullName && <p className={styles.errorMessage}>{profileErrors.fullName}</p>}
        </div>

        <div className={styles.formGroup}>
          <label htmlFor="email">{t('email_label', 'Email')}</label>
          <input
            type="email"
            id="email"
            name="email"
            value={profile.email}
            onChange={handleProfileChange}
            className={styles.formInput}
            disabled
            title={t('email_readonly_tooltip', 'Email cannot be changed')}
          />
          {profileErrors.email && <p className={styles.errorMessage}>{profileErrors.email}</p>}
        </div>

        <div className={styles.formGroup}>
          <label htmlFor="phoneNumber">{t('customer_phone_label', 'Phone Number')}</label>
          <input type="tel" id="phoneNumber" name="phoneNumber" value={profile.phoneNumber} onChange={handleProfileChange} className={styles.formInput} />
          {profileErrors.phoneNumber && <p className={styles.errorMessage}>{profileErrors.phoneNumber}</p>}
        </div>

        <div className={styles.formActions}>
          <button type="submit" className={styles.saveButton}>{t('save_changes_button', 'Save Changes')}</button>
        </div>
      </form>
    </section>
  );
}
