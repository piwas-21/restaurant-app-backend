"use client";

import React from 'react';
import { useCookieConsent } from "./CookieConsentContext";
import { useTranslation } from "react-i18next";

export default function FooterCookieLink() {
  const { openSettingsModal } = useCookieConsent();
  const { t } = useTranslation();

  return (
    <button 
      onClick={openSettingsModal} 
      style={{ 
        background: 'none', 
        border: 'none', 
        color: 'var(--text-color)',
        textDecoration: 'underline', 
        cursor: 'pointer', 
        padding: '0.5rem 0',
        fontSize: '0.9rem'
      }}
    >
      {t('cookie_settings_footer_link', 'Manage Cookie Preferences')}
    </button>
  );
}
