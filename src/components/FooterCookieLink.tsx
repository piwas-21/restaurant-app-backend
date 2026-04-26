"use client";

import React, { useState, useEffect } from 'react';
import { useCookieConsent } from "./CookieConsentContext";
import { useTranslation } from "react-i18next";

export default function FooterCookieLink() {
  const { openSettingsModal } = useCookieConsent();
  const { t } = useTranslation();
  const [isMounted, setIsMounted] = useState(false);

  useEffect(() => {
    setIsMounted(true);
  }, []);

  // Use fallback text before mount, and translated text after mount
  const buttonText = isMounted
    ? t('cookie_settings_footer_link', 'Manage Cookie Preferences')
    : 'Manage Cookie Preferences';

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
      {buttonText}
    </button>
  );
}
