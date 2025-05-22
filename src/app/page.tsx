"use client";

import React, { useState, useEffect } from "react";
import styles from "./styles/HomePage.module.css";
import Link from "next/link";
import { useTranslation } from "react-i18next";
import FooterCookieLink from "@/components/FooterCookieLink";

export default function HomePage() {
  const { t, i18n } = useTranslation();
  const [isClient, setIsClient] = useState(false);

  useEffect(() => {
    setIsClient(true);
  }, []);

  useEffect(() => {
    if (isClient) {
      document.title = t("home_page_title");
    }
  }, [isClient, t, i18n.language]);

  const googleMapsEmbedUrl = "https://www.google.com/maps/embed?pb=!1m18!1m12!1m3!1d2761.009531572909!2d6.139046315578307!3d46.21753897911699!2m3!1f0!2f0!3f0!3m2!1i1024!2i768!4f13.1!3m3!1m2!1s0x478c6508101d6e5f%3A0x34a0d1c0b2f5c303!2sRue%20du%20Grand-Pr%C3%A9%2045%2C%201202%20Gen%C3%A8ve%2C%20Switzerland!5e0";
  const backgroundImageUrl = 'https://lh3.google.com/u/0/d/1ZnD-FSyYYeRA9nreAAuFtD-0IMgwTI9_';

  return (
    <div className={styles.homeContainer}>
      <section 
        className={styles.heroHeaderSection} 
        style={{ backgroundImage: `url(${backgroundImageUrl})` }}
        aria-labelledby="hero-heading"
      >
        <div className={styles.glassOverlay}></div>
        <div className={styles.heroContent}>
          <h1 id="hero-heading" className={styles.heroTitle}>{isClient ? t("home_hero_title") : "Discover Authentic Turkish Flavors"}</h1>
          <p className={styles.heroSubtitle}>{isClient ? t("home_hero_subtitle") : "Your journey into rich tastes and traditions begins here."}</p>
          <div className={styles.ctaButtons}>
              <Link href="/menu" className={styles.ctaButtonPrimary} role="button">{isClient ? t("home_menu_cta") : "View Menu"}</Link>
              <Link href="/reservations" className={styles.ctaButtonSecondary} role="button">{isClient ? t("home_reservations_cta") : "Book a Table"}</Link>
          </div>
        </div>
      </section>

      <div className={styles.pageContentWrapper}> 
        <section className={styles.storySection} aria-labelledby="story-heading">
          <h2 id="story-heading">{isClient ? t("home_story_title") : "Our Story"}</h2>
          <p>{isClient ? t("home_story_content") : "RUMI is more than just a restaurant; it's a place where Turkish culinary traditions are celebrated with a modern twist. Our chefs use the freshest ingredients to bring you an unforgettable dining experience."}</p>
        </section>

        <section className={styles.openingHoursSection} aria-labelledby="hours-heading">
          <h2 id="hours-heading">{isClient ? t("home_opening_hours_title") : "Opening Hours"}</h2>
          <p>
            {isClient ? t("home_opening_hours_days_1") : "Monday - Saturday"}: {isClient ? t("home_opening_hours_time_1") : "11:00 AM - 10:00 PM"}<br/>
            {isClient ? t("home_opening_hours_days_2") : "Sunday"}: {isClient ? t("home_opening_hours_time_2") : "12:00 PM - 9:00 PM"}
          </p>
        </section>

        <section className={styles.locationSection} aria-labelledby="location-heading">
          <h2 id="location-heading">{isClient ? t("home_location_title") : "Visit Us"}</h2>
          <address>
            {isClient ? t("rumi_address_street") : "Rue du Grand-Pré 45"}<br/>
            {isClient ? t("rumi_address_city_country") : "1202 Genève, Switzerland"}<br/>
            {isClient ? t("phone_label") : "Phone"}: <a href={`tel:${t("rumi_phone_number").replace(/\s/g, "")}`}>{isClient ? t("rumi_phone_number") : "+41 22 786 33 33"}</a>
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
              title={isClient ? t("google_maps_iframe_title") : "Location of RUMI Restaurant"}
              aria-label={isClient ? t("google_maps_iframe_aria_label") : "Google Maps showing location of RUMI Restaurant"}
            ></iframe>
          </div>
        </section>

        <footer className={styles.homeFooter}>
           <p>&copy; {new Date().getFullYear()} RUMI Restaurant. All rights reserved.</p>
           <p>{isClient ? t("rumi_address_street") : "Rue du Grand-Pré 45"}, {isClient ? t("rumi_address_city_country") : "1202 Genève, Switzerland"}</p>
           <FooterCookieLink />
        </footer>
      </div>
    </div>
  );
}
