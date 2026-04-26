// src/components/LanguageSwitcher.tsx
"use client";

import React, { useState, useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import Image from 'next/image';
import styles from "../app/styles/LanguageSwitcher.module.css";
import { SUPPORTED_LANGUAGES, LanguageCode } from "@/config/languageConfig";

const languages = SUPPORTED_LANGUAGES;

export type { LanguageCode };

export default function LanguageSwitcher() {
  const { i18n } = useTranslation();
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const changeLanguage = (lng: LanguageCode) => {
    i18n.changeLanguage(lng);
    // Language preference is essential functionality, always save it
    // This is typically not considered a tracking/preference cookie
    localStorage.setItem("i18nextLng", lng);
    setDropdownOpen(false);
  };

  const toggleDropdown = () => {
    setDropdownOpen(!dropdownOpen);
  };

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setDropdownOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
    };
  }, [dropdownRef]);

  // Fallback to English if current language details are not found (should not happen with proper setup)
  const currentLanguageDetails = languages.find(l => l.code === i18n.resolvedLanguage) || languages.find(l => l.code === 'en') || languages[0];

  const listRef = useRef<HTMLUListElement>(null);
  const [showScrollIndicator, setShowScrollIndicator] = useState(false);

  useEffect(() => {
    if (dropdownOpen && listRef.current) {
      // Check if scrollable
      const { scrollHeight, clientHeight } = listRef.current;
      setShowScrollIndicator(scrollHeight > clientHeight);
    }
  }, [dropdownOpen]);

  const handleScroll = () => {
    if (listRef.current) {
      const { scrollTop, scrollHeight, clientHeight } = listRef.current;
      // Hide indicator if near bottom
      const isNearBottom = scrollTop + clientHeight >= scrollHeight - 5;
      setShowScrollIndicator(!isNearBottom);
    }
  };

  const scrollDown = (e: React.MouseEvent) => {
    e.stopPropagation(); // Prevent dropdown from closing
    if (listRef.current) {
      listRef.current.scrollBy({ top: 100, behavior: 'smooth' });
    }
  };

  return (
    <div className={styles.languageSwitcher} ref={dropdownRef}>
      <button
        onClick={toggleDropdown}
        className={styles.dropdownToggle}
        aria-haspopup="listbox"
        aria-expanded={dropdownOpen}
        aria-label="Toggle language menu"
      >
        <Image src={currentLanguageDetails.flag} alt={currentLanguageDetails.name} width={24} height={18} />
        <span className={styles.languageName}>{currentLanguageDetails.code.toUpperCase()}</span>
        <span className={styles.arrow}>{dropdownOpen ? "▲" : "▼"}</span>
      </button>
      {dropdownOpen && (
        <div className={styles.dropdownContainer}>
          <ul
            className={styles.dropdownMenu}
            role="listbox"
            ref={listRef}
            onScroll={handleScroll}
          >
            {languages.map((language) => (
              <li key={language.code} role="option" aria-selected={language.code === i18n.resolvedLanguage}>
                <button
                  onClick={() => changeLanguage(language.code)}
                  className={styles.dropdownItem}
                >
                  <Image src={language.flag} alt={language.name} width={20} height={15} />
                  <span>{language.name}</span>
                </button>
              </li>
            ))}
          </ul>
          {showScrollIndicator && (
             <div className={styles.scrollIndicator} onClick={scrollDown} role="button" aria-label="Scroll down">
               ▼
             </div>
          )}
        </div>
      )}
    </div>
  );
}
