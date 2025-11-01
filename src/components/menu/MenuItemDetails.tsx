"use client";

import React from "react";
import styles from "./MenuItemDetails.module.css";
import AllergenDisplay from "@/components/common/AllergenDisplay";

type RatingData = { average: number; count: number } | undefined;

type Props = {
  id: string;
  title: string;
  description: string;
  ingredients?: string;
  allergens?: string[];
  price: number;
  dietaryTags: string[];
  t: (key: string, defaultValue?: any) => string;
  initialRatingData?: RatingData;
};

export default function MenuItemDetails({ id, title, description, ingredients, allergens, price, dietaryTags, t, initialRatingData }: Props) {
  return (
    <>
      <h3 id={`item-name-${id}`} className={styles.itemTitle}>{title}</h3>
      {/* <p className={`${styles.itemDescription} ${styles.clamp2}`}>{(description || '').trim().length > 0 ? description : ' '}</p> */}
      {/* {(() => {
        const text = (ingredients || '').trim();
        const parts = text
          ? text.split(/[\,\n;]+/).map((s) => s.trim()).filter(Boolean)
          : [];
        const max = 3; // Limit to 3 ingredients for single line display
        const shown = parts.slice(0, max);
        const remaining = parts.length - shown.length;
        return (
          <div className={styles.ingredientsSection} aria-label={t('ingredients')}>
            {parts.length > 0 ? (
              <>
                <div className={styles.ingredientsLabel}>{t('ingredients')}</div>
                <div className={styles.ingredientsContent}>
                  {shown.map((p, idx) => (
                    <span key={`${id}-ing-${idx}`} className={styles.ingredientTag}>
                      {p}
                    </span>
                  ))}
                  {remaining > 0 && (
                    <span
                      className={styles.ingredientTag}
                      title={`+${remaining} more ingredients: ${parts.slice(max).join(', ')}`}
                    >
                      +{remaining}
                    </span>
                  )}
                </div>
              </>
            ) : (
              // Preserve full section height when no ingredients
              <>
                <div className={styles.ingredientsLabel} style={{ visibility: 'hidden' }}>{t('ingredients')}</div>
                <div className={styles.ingredientsContent} style={{ visibility: 'hidden' }}>
                  <span className={styles.ingredientTag}>placeholder</span>
                </div>
              </>
            )}
          </div>
        );
      })()} */}

      {/* Allergens section - display below ingredients */}
      <AllergenDisplay
        allergens={allergens}
        id={id}
        maxVisible={3}
        showLabel={true}
        variant="full"
      />

      <p
        className={styles.itemPrice}
        aria-label={`${t("checkout_total_label")} CHF ${price.toFixed(2)}`}
      >
        CHF {price.toFixed(2)}
      </p>

      {/* <AverageRating dishId={id} initialRatingData={initialRatingData} /> */}

      {dietaryTags && dietaryTags.length > 0 && (
        <div className={styles.allergyTags} aria-label={t("dietary_information_label")}>
          {dietaryTags.map((tag) => (
            <span
              key={tag}
              className={`${styles.allergyTag} ${styles[(tag || "").toLowerCase().replace(/\s+/g, "-")] || ""}`}
              role="status"
            >
              {t(tag, tag)}
            </span>
          ))}
        </div>
      )}
    </>
  );
}
