"use client";

import React from 'react';
import { useCookieConsent } from './CookieConsentContext';
import styles from '../app/styles/CookieConsentBanner.module.css';
import { useTranslation } from 'react-i18next';

export default function CookieConsentBanner() {
  const { consent, isConsentPending, acceptPreferences, declinePreferences } = useCookieConsent();
  const { t } = useTranslation();

  // Don't show the banner if consent is no longer pending (i.e., already set or loaded)
  // Or if consent for preferences has already been explicitly given or denied
  if (!isConsentPending && consent.preferences !== null) {
    return null;
  }

  // Also don't show if still loading initial consent state, unless it's determined that preferences is null
  if (isConsentPending && consent.preferences !== null) {
      return null;
  }
  
  // Only show when consent is loaded (isConsentPending is false) AND preferences is still null
  if (isConsentPending || consent.preferences !== null) {
    return null;
  }

  return (
    <div className={styles.bannerContainer}>
      <div className={styles.bannerContent}>
        <p className={styles.bannerText}>
          {t('cookie_consent_banner_text', 'We use cookies to enhance your experience. By continuing to visit this site you agree to our use of cookies. For now, this only includes remembering your language preference.')}
        </p>
        <div className={styles.bannerActions}>
          <button onClick={acceptPreferences} className={`${styles.bannerButton} ${styles.acceptButton}`}>
            {t('cookie_consent_accept', 'Accept')}
          </button>
          <button onClick={declinePreferences} className={`${styles.bannerButton} ${styles.declineButton}`}>
            {t('cookie_consent_decline', 'Decline')}
          </button>
          {/* <button onClick={openSettings} className={styles.settingsButton}>Settings</button> // For future expansion */}
        </div>
      </div>
    </div>
  );
}