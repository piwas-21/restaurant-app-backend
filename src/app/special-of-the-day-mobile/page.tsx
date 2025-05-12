// src/app/special-of-the-day-mobile/page.tsx
"use client";

import React from "react";
// import type { Metadata } from "next"; // Static metadata
import Link from "next/link";
import Image from "next/image";
import styles from "../styles/SpecialOfTheDayMobile.module.css";
import { useTranslation } from "react-i18next"; // Import useTranslation

// export const metadata: Metadata = {
//   title: "Special of the Day - RUMI Restaurant Mobile",
//   description: "Check out today"s special dish at RUMI Restaurant, Geneva.",
// };

// Placeholder for actual special data - this would come from an API or admin panel
// Ensure these keys match your translation files
const specialDishData = {
  id: "iskender_kebab_special", // Used for linking, not directly translated here
  name_key: "iskender_kebab_special_name",
  description_key: "iskender_kebab_special_description",
  price: "29.50",
  original_price: "32.00",
  image_url: "/placeholder-iskender.jpg",
  allergy_labels_keys: ["Halal", "Contains Gluten", "Contains Dairy"], // These can be translated if they are keys
  availability_key: "special_availability_today_only", // Example key, ensure it"s in translations
};

export default function SpecialOfTheDayMobilePage() {
  const { t } = useTranslation(); // Initialize useTranslation

  // Determine if there is a special dish to display
  const currentSpecial = specialDishData; // Change to noSpecialDish to test the other case

  return (
    <main className={styles.specialContainerMobile} aria-labelledby="special-heading">
      <header className={styles.specialHeaderMobile}>
        <Link href="/welcome-mobile" className={styles.backButtonSpecialMobile} aria-label={t("back_to_welcome")}>
          &larr;
        </Link>
        <h1 id="special-heading">{t("special_of_the_day_title")}</h1>
        <span style={{width: "40px"}}></span> {/* Spacer */}
      </header>

      {currentSpecial ? (
        <article className={styles.specialContentMobile}>
          <div className={styles.specialImageContainerMobile}>
            <Image 
              src={currentSpecial.image_url} 
              alt={t(currentSpecial.name_key)} 
              width={300} 
              height={200} 
              className={styles.specialImageMobile} 
              priority 
            />
            {currentSpecial.availability_key && (
              <span className={styles.availabilityTagMobile}>{t(currentSpecial.availability_key)}</span>
            )}
          </div>

          <h2 className={styles.specialNameMobile}>{t(currentSpecial.name_key)}</h2>
          <p className={styles.specialDescriptionMobile}>{t(currentSpecial.description_key)}</p>
          
          <div className={styles.priceSectionMobile}>
            <span className={styles.currentPriceMobile}>CHF {currentSpecial.price}</span>
            {currentSpecial.original_price && (
              <span className={styles.originalPriceMobile}>CHF {currentSpecial.original_price}</span>
            )}
          </div>

          {currentSpecial.allergy_labels_keys && currentSpecial.allergy_labels_keys.length > 0 && (
            <div className={styles.allergyInfoMobile} aria-label={t("allergens_label")}>
              <strong>{t("allergens_label")}</strong> 
              {currentSpecial.allergy_labels_keys.map(key => t(key, key)).join(", ")}
            </div>
          )}

          <div className={styles.specialActionsMobile}>
            {/* The link might need to pass the item ID to the menu page for highlighting or direct add */}
            <Link href={`/menu-mobile?highlight=${currentSpecial.id}`} className={styles.orderSpecialButtonMobile}>
              {t("order_special_now_button")}
            </Link>
            <Link href="/menu-mobile" className={styles.viewFullMenuButtonMobile}>
              {t("view_full_menu_button")}
            </Link>
          </div>
        </article>
      ) : (
        <div className={styles.noSpecialMessageMobile}>
          <p>{t("no_special_today_message")}</p>
          <Link href="/menu-mobile" className={styles.viewFullMenuButtonMobile}>
            {t("view_full_menu_button")}
          </Link>
        </div>
      )}
    </main>
  );
}

