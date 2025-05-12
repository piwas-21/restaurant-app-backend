// src/components/LanguageSwitcher.tsx
"use client";

import React, { useState, useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import Image from 'next/image';
import styles from "../app/styles/LanguageSwitcher.module.css";

// ** Important: Ensure you have flag images at these paths **
// e.g., /public/flags/en.svg, /public/flags/de.svg, /public/flags/tr.svg
const languages = [
  { code: "en", name: "English", flag: "/flags/en.svg" },
  { code: "de", name: "Deutsch", flag: "/flags/de.svg" },
  { code: "tr", name: "Türkçe", flag: "/flags/tr.svg" },
];

export default function LanguageSwitcher() {
  const { i18n } = useTranslation();
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const changeLanguage = (lng: string) => {
    i18n.changeLanguage(lng);
    setDropdownOpen(false); // Close dropdown after selection
  };

  const toggleDropdown = () => {
    setDropdownOpen(!dropdownOpen);
  };

  // Close dropdown if clicked outside
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setDropdownOpen(false);
      }
    }
    // Bind the event listener
    document.addEventListener("mousedown", handleClickOutside);
    return () => {
      // Unbind the event listener on clean up
      document.removeEventListener("mousedown", handleClickOutside);
    };
  }, [dropdownRef]);

  const currentLanguage = languages.find(l => l.code === i18n.resolvedLanguage) || languages[0];

  return (
    <div className={styles.languageSwitcherContainer} ref={dropdownRef}>
      <button 
        className={styles.dropdownButton}
        onClick={toggleDropdown}
        aria-haspopup="true"
        aria-expanded={dropdownOpen}
        aria-label={`Change language, current language is ${currentLanguage.name}`}
      >
        {/* Display current language flag */}
        <Image src={currentLanguage.flag} alt={currentLanguage.name} width={24} height={18} className={styles.flagIcon} />
        {/* Optional: Add a dropdown icon/caret */}
        <span className={styles.caret}>▼</span>
      </button>
      
      {dropdownOpen && (
        <ul className={styles.dropdownMenu} role="menu">
          {languages.map((lang) => (
            <li key={lang.code} role="menuitem">
              <button
                onClick={() => changeLanguage(lang.code)}
                disabled={i18n.resolvedLanguage === lang.code} // Keep disabled state
                className={`${styles.dropdownItem} ${i18n.resolvedLanguage === lang.code ? styles.activeLang : ""}`}
              >
                <Image src={lang.flag} alt={lang.name} width={24} height={18} className={styles.flagIcon} />
                {/* Optional: Display name next to flag if desired */}
                {/* <span className={styles.langName}>{lang.name}</span> */} 
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
