"use client";

import React from "react";
import styles from "@/app/styles/MenuPage.module.css";

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

// Helper function to get allergen styling and icon
function getAllergenInfo(allergen: string) {
  const allergenLower = allergen.toLowerCase();

  // Define allergen types with their styling and icons
  const allergenMap: { [key: string]: { icon: string; className: string } } = {
    'vegan': { icon: '🌱', className: 'vegan' },
    'vegetarian': { icon: '🥬', className: 'vegetarian' },
    'gluten-free': { icon: '🌾', className: 'glutenFree' },
    'gluten free': { icon: '🌾', className: 'glutenFree' },
    'dairy-free': { icon: '🥛', className: 'dairyFree' },
    'dairy free': { icon: '🥛', className: 'dairyFree' },
    'nut-free': { icon: '🥜', className: 'nutFree' },
    'nut free': { icon: '🥜', className: 'nutFree' },
    'halal': { icon: '☪️', className: 'halal' },
    'kosher': { icon: '✡️', className: 'kosher' },
    'contains nuts': { icon: '⚠️', className: 'warning' },
    'contains dairy': { icon: '⚠️', className: 'warning' },
    'contains gluten': { icon: '⚠️', className: 'warning' },
    'contains soy': { icon: '⚠️', className: 'warning' },
    'contains eggs': { icon: '⚠️', className: 'warning' },
    'spicy': { icon: '🌶️', className: 'spicy' },
    'sugar-free': { icon: '🍯', className: 'sugarFree' },
    'organic': { icon: '🌿', className: 'organic' },
    'low-sodium': { icon: '🧂', className: 'lowSodium' }
  };

  // Check for exact matches first
  if (allergenMap[allergenLower]) {
    return allergenMap[allergenLower];
  }

  // Check for partial matches for "contains" warnings
  if (allergenLower.includes('contain') || allergenLower.includes('may contain')) {
    return { icon: '⚠️', className: 'warning' };
  }

  // Default styling for unknown allergens
  return { icon: '🏷️', className: 'default' };
}

export default function MenuItemDetails({ id, title, description, ingredients, allergens, price, dietaryTags, t, initialRatingData }: Props) {
  return (
    <>
      <h3 id={`item-name-${id}`} className={styles.itemTitle}>{title}</h3>
      <p className={`${styles.itemDescription} ${styles.clamp2}`}>{(description || '').trim().length > 0 ? description : ' '}</p>
      {(() => {
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
      })()}

      {/* Allergens section - display below ingredients */}
      <div className={styles.allergensSection} aria-label={t('allergens', 'Allergens')}>
        {allergens && allergens.length > 0 ? (
          <>
            <div className={styles.allergensLabel}>{t('allergens', 'Allergens')}</div>
            <div className={styles.allergensContent}>
              {(() => {
                const max = 3; // Limit to 3 allergens for single line display
                const shown = allergens.slice(0, max);
                const remaining = allergens.length - shown.length;
                return (
                  <>
                    {shown.map((allergen, idx) => {
                      const { icon, className } = getAllergenInfo(allergen);
                      return (
                        <span
                          key={`${id}-allergen-${idx}`}
                          className={`${styles.allergenTag} ${styles[className]}`}
                          title={allergen}
                        >
                          <span className={styles.allergenIcon}>{icon}</span>
                          <span className={styles.allergenText}>{allergen}</span>
                        </span>
                      );
                    })}
                    {remaining > 0 && (
                      <span
                        className={`${styles.allergenTag} ${styles.more}`}
                        title={`+${remaining} more allergens: ${allergens.slice(max).join(', ')}`}
                      >
                        +{remaining}
                      </span>
                    )}
                  </>
                );
              })()}
            </div>
          </>
        ) : (
          // Preserve full section height when no allergens
          <>
            <div className={styles.allergensLabel} style={{ visibility: 'hidden' }}>{t('allergens', 'Allergens')}</div>
            <div className={styles.allergensContent} style={{ visibility: 'hidden' }}>
              <span className={styles.allergenTag}>placeholder</span>
            </div>
          </>
        )}
      </div>

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
