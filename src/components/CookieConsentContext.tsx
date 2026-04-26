"use client";

import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';

const COOKIE_CONSENT_KEY = 'rumi_cookie_consent';

interface ConsentState {
  preferences: boolean | null; // null = not set, true = accepted, false = declined
  // Add other categories here later e.g., analytics: boolean | null;
}

interface CookieConsentContextType {
  consent: ConsentState;
  isConsentPending: boolean;
  isSettingsModalOpen: boolean;
  acceptPreferences: () => void;
  declinePreferences: () => void;
  updateConsent: (newConsent: Partial<ConsentState>) => void; // Expose for modal
  openSettingsModal: () => void;
  closeSettingsModal: () => void;
}

const CookieConsentContext = createContext<CookieConsentContextType | undefined>(undefined);

export const CookieConsentProvider = ({ children }: { children: ReactNode }) => {
  const [consent, setConsent] = useState<ConsentState>({ preferences: null });
  const [isConsentPending, setIsConsentPending] = useState(true);
  const [isSettingsModalOpen, setIsSettingsModalOpen] = useState(false);

  useEffect(() => {
    const storedConsent = localStorage.getItem(COOKIE_CONSENT_KEY);
    if (storedConsent) {
      try {
        const parsedConsent = JSON.parse(storedConsent) as ConsentState;
        setConsent(parsedConsent);
      } catch (error) {
        console.error("Error parsing stored cookie consent:", error);
        localStorage.removeItem(COOKIE_CONSENT_KEY);
      }
    }
    setIsConsentPending(false);
  }, []);

  const updateConsentStateAndStorage = (newConsentSettings: Partial<ConsentState>) => {
    setConsent(prevConsent => {
      const updatedConsent = { ...prevConsent, ...newConsentSettings };
      localStorage.setItem(COOKIE_CONSENT_KEY, JSON.stringify(updatedConsent));
      // If preferences are declined, ensure i18nextLng is removed.
      // If accepted, it will be set by LanguageSwitcher upon language change.
      if (updatedConsent.preferences === false) {
        localStorage.removeItem("i18nextLng");
      }
      return updatedConsent;
    });
    setIsConsentPending(false);
  };

  const acceptPreferences = () => {
    updateConsentStateAndStorage({ preferences: true });
  };

  const declinePreferences = () => {
    updateConsentStateAndStorage({ preferences: false });
  };

  const openSettingsModal = () => setIsSettingsModalOpen(true);
  const closeSettingsModal = () => setIsSettingsModalOpen(false);

  return (
    <CookieConsentContext.Provider value={{
      consent,
      isConsentPending,
      isSettingsModalOpen,
      acceptPreferences,
      declinePreferences,
      updateConsent: updateConsentStateAndStorage,
      openSettingsModal,
      closeSettingsModal
    }}>
      {children}
    </CookieConsentContext.Provider>
  );
};

export const useCookieConsent = () => {
  const context = useContext(CookieConsentContext);
  if (context === undefined) {
    throw new Error('useCookieConsent must be used within a CookieConsentProvider');
  }
  return context;
};
