"use client";

import React, { useState, useEffect } from 'react';
import { useCookieConsent } from './CookieConsentContext';
import styles from '../app/styles/CookieSettingsModal.module.css';
import { useTranslation } from 'react-i18next';

export default function CookieSettingsModal() {
  const { consent, isSettingsModalOpen, closeSettingsModal, updateConsent } = useCookieConsent();
  const { t } = useTranslation();
  const [currentPreferences, setCurrentPreferences] = useState(consent.preferences);

  useEffect(() => {
    setCurrentPreferences(consent.preferences);
  }, [consent.preferences, isSettingsModalOpen]);

  if (!isSettingsModalOpen) {
    return null;
  }

  const handlePreferenceChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setCurrentPreferences(event.target.checked);
  };

  const handleSaveChanges = () => {
    updateConsent({ preferences: currentPreferences });
    closeSettingsModal();
  };

  return (
    <div className={styles.modalOverlay} onClick={closeSettingsModal}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2>{t('cookie_settings_title', 'Cookie Settings')}</h2>
          <button onClick={closeSettingsModal} className={styles.closeButton} aria-label={t('cookie_settings_close_aria', 'Close settings')}>&times;</button>
        </div>
        <div className={styles.modalBody}>
          <p>{t('cookie_settings_intro', 'Manage your cookie preferences. You can enable or disable specific cookie categories below.')}</p>
          
          <div className={styles.preferenceItem}>
            <div className={styles.preferenceDetails}>
              <strong>{t('cookie_settings_preferences_title', 'Preferences')}</strong>
              <p>{t('cookie_settings_preferences_desc', 'These cookies remember your choices, like language or theme, to personalize your experience.')}</p>
            </div>
            <label className={styles.toggleSwitch}>
              <input 
                type="checkbox" 
                checked={currentPreferences === true}
                onChange={handlePreferenceChange} 
              />
              <span className={styles.slider}></span>
            </label>
          </div>

          {/* Future categories can be added here, e.g.:
          <div className={styles.preferenceItem}>
            <strong>Analytics Cookies</strong>
            <p>These help us understand how you use our site.</p>
            <label className={styles.toggleSwitch}>
              <input type="checkbox" checked={...} onChange={...} />
              <span className={styles.slider}></span>
            </label>
          </div>
          */}
        </div>
        <div className={styles.modalFooter}>
          <button onClick={handleSaveChanges} className={`${styles.modalButton} ${styles.saveButton}`}>
            {t('cookie_settings_save', 'Save Preferences')}
          </button>
        </div>
      </div>
    </div>
  );
}
