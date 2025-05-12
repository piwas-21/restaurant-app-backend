// src/app/welcome-mobile/page.tsx
"use client";

import React from "react";
import Link from "next/link";
import Image from "next/image";
import styles from "../styles/WelcomeMobile.module.css";
import { useTranslation } from "react-i18next"; // Import useTranslation
import LanguageSwitcher from "@/components/LanguageSwitcher"; // Import LanguageSwitcher

const logoPath = "/rumi_logo.png";

export default function WelcomeMobilePage() {
  const { t } = useTranslation(); // Initialize useTranslation

  return (
    <main className={styles.welcomeContainer}>
      <div className={styles.logoContainer}>
        <Image src={logoPath} alt="RUMI Restaurant Logo" width={150} height={150} priority />
      </div>

      <h1 className={styles.welcomeTitle}>{t("welcome_to_rumi")}</h1>
      <p className={styles.welcomeSubtitle}>{t("authentic_turkish_cuisine")}</p>

      {/* Use the LanguageSwitcher component */}
      <div className={`${styles.languageSelectorContainer} ${styles.welcomeMobile}`}>
        <p className={styles.languagePrompt}>{t("select_language")}:</p>
        <LanguageSwitcher />
      </div>

      <div className={styles.ctaContainer}>
        <Link href="/menu-mobile" className={styles.ctaButtonPrimary}>
          {t("view_menu_order")}
        </Link>
        <Link href="/reservations-mobile" className={styles.ctaButtonSecondary}>
          {t("reserve_table")}
        </Link>
      </div>

      <footer className={styles.footer}>
        <p>{t("copyright_rumi", { year: new Date().getFullYear() })}</p>
        <p><Link href="/">{t("view_full_website")}</Link></p>
      </footer>
    </main>
  );
}

