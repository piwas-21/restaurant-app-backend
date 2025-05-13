// src/app/page.tsx
"use client";

import React, { useState, useEffect } from "react";
import styles from "./styles/HomePage.module.css";
import Link from "next/link";
import Image from "next/image";
import { useTranslation } from "react-i18next";
import { usePathname } from "next/navigation";
import LanguageSwitcher from "@/components/LanguageSwitcher"; 
import ThemeSwitcher from "@/components/ThemeSwitcher";

export default function HomePage() {
  const { t, i18n } = useTranslation();
  const pathname = usePathname();
  const [isClient, setIsClient] = useState(false);

  useEffect(() => {
    setIsClient(true);
    // Setting document.title can also cause hydration issues if t() is not ready
    // It's safer to set it after isClient is true or inside the main return if !isClient is handled
  }, []);

  // Update document title after client is confirmed and translations are ready
  useEffect(() => {
    if (isClient) {
      document.title = t("home_page_title");
    }
  }, [isClient, t, i18n.language]);

  const googleMapsEmbedUrl = "https://www.google.com/maps/embed?pb=!1m18!1m12!1m3!1d2761.9879077000003!2d6.1423647!3d46.2093549!2m3!1f0!2f0!3f0!3m2!1i1024!2i768!4f13.1!3m3!1m2!1s0x478c6527ab39c7f7%3A0x1d5b380909c0e60a!2sRue%20de%20Berne%2013%2C%201201%20Gen%C3%A8ve%2C%20Switzerland!5e0!3m2!1sen!2sch!4v1715517619196!5m2!1sen!2sch";
  const backgroundImageUrl = '/images/rumi-background.png'; // Ensure this path is correct

  if (!isClient) {
    // Render a minimal skeleton or null to avoid hydration mismatch
    // This ensures server and initial client render are the same before translations load.
    return (
      <div className={styles.homeContainer}>
        <section 
          className={styles.heroHeaderSection} 
          style={{ backgroundImage: `url(${backgroundImageUrl})` }}
        >
          <div className={styles.glassOverlay}></div>
          <nav className={styles.heroNav}>
            <Link href="/" className={styles.heroLogoLink}>
              {/* <Image src="/rumi_logo_transparent.png" alt="RUMI Restaurant Logo" width={180} height={90} /> */}
            </Link>
            {/* Placeholder for nav links to match structure if needed */}
            <div className={styles.heroNavLinks}>
                <span className={`nav-link`}>Home</span>
                <span className={`nav-link`}>Menu</span>
                <span className={`nav-link`}>Reservations</span>
                <span className={`nav-link`}>Cart</span>
            </div>
          </nav>
          <div className={styles.heroContent}>
            {/* Placeholder for hero title to match structure */}
            <h1 id="hero-heading" className={styles.heroTitle}>&nbsp;</h1> 
          </div>
        </section>
      </div>
    ); 
  }

  return (
    <div className={styles.homeContainer}>
      <section 
        className={styles.heroHeaderSection} 
        style={{ backgroundImage: `url(${backgroundImageUrl})` }}
        aria-labelledby="hero-heading"
      >
        <div className={styles.glassOverlay}></div>
        
        <nav className={styles.heroNav}>
          <Link href="/" className={styles.heroLogoLink}>
            {/* <Image src="/rumi_logo_transparent.png" alt="RUMI Restaurant Logo" width={180} height={90} /> */}
          </Link>
          <div className={styles.heroNavLinks}>
            <Link href="/" className={`nav-link ${pathname === '/' ? 'active' : ''}`}>{t('nav_home', 'Home')}</Link>
            <Link href="/menu" className={`nav-link ${pathname === '/menu' ? 'active' : ''}`}>{t('nav_menu', 'Menu')}</Link>
            <Link href="/reservations" className={`nav-link ${pathname === '/reservations' ? 'active' : ''}`}>{t('nav_reservations', 'Reservations')}</Link>
            <Link href="/cart" className={`nav-link ${pathname === '/cart' ? 'active' : ''}`}>{t('nav_cart', 'Cart')}</Link>
            <LanguageSwitcher />
            <ThemeSwitcher />
          </div>
        </nav>

        <div className={styles.heroContent}>
          <h1 id="hero-heading" className={styles.heroTitle}>{t("home_hero_title")}</h1>
          <p className={styles.heroSubtitle}>{t("home_hero_subtitle")}</p>
          <div className={styles.ctaButtons}>
              <Link href="/menu" className={styles.ctaButtonPrimary} role="button">{t("home_menu_cta")}</Link>
              <Link href="/reservations" className={styles.ctaButtonSecondary} role="button">{t("home_reservations_cta")}</Link>
          </div>
        </div>
      </section>

      <div className={styles.pageContentWrapper}> 
        <section className={styles.storySection} aria-labelledby="story-heading">
          <h2 id="story-heading">{t("home_story_title")}</h2>
          <p>{t("home_story_content")}</p>
        </section>

        <section className={styles.openingHoursSection} aria-labelledby="hours-heading">
          <h2 id="hours-heading">{t("home_opening_hours_title")}</h2>
          <p>
            {t("home_opening_hours_days_1")}: {t("home_opening_hours_time_1")}<br/>
            {t("home_opening_hours_days_2")}: {t("home_opening_hours_time_2")}
          </p>
        </section>

        <section className={styles.locationSection} aria-labelledby="location-heading">
          <h2 id="location-heading">{t("home_location_title")}</h2>
          <address>
            {t("rumi_address_street")}<br/>
            {t("rumi_address_city_country")}<br/>
            {t("phone_label")}: <a href={`tel:${t("rumi_phone_number").replace(/\s/g, "")}`}>{t("rumi_phone_number")}</a>
          </address>
          <div className={styles.mapContainer}>
            <iframe 
              src={googleMapsEmbedUrl}
              width="100%" 
              height="450" 
              style={{border:0}} 
              allowFullScreen={true} 
              loading="lazy" 
              referrerPolicy="no-referrer-when-downgrade"
              title={t("google_maps_iframe_title")}
              aria-label={t("google_maps_iframe_aria_label")}
            ></iframe>
          </div>
        </section>

        <footer className={styles.homeFooter}>
           <p>&copy; {new Date().getFullYear()} RUMI Restaurant. All rights reserved.</p>
           <p>Rue de Berne 13, 1201 Genève, Switzerland</p>
        </footer>
      </div>
    </div>
  );
}
