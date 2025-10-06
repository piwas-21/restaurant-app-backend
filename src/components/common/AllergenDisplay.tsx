'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/MenuPage.module.css';

// Helper function to get allergen styling and icon
export function getAllergenInfo(allergen: string) {
  const allergenLower = allergen.toLowerCase();

  // Define allergen types with their styling and icons
  const allergenMap: { [key: string]: { icon: string; className: string } } = {
    vegan: { icon: '🌱', className: 'vegan' },
    vegetarian: { icon: '🥬', className: 'vegetarian' },
    'gluten free': { icon: '🌾', className: 'glutenFree' },
    'dairy free': { icon: '🥛', className: 'dairyFree' },
    'nut free': { icon: '🥜', className: 'nutFree' },
    halal: { icon: '☪️', className: 'halal' },
    kosher: { icon: '✡️', className: 'kosher' },
    'contains nuts': { icon: '⚠️', className: 'warning' },
    'contains dairy': { icon: '⚠️', className: 'warning' },
    'contains gluten': { icon: '⚠️', className: 'warning' },
    'contains soy': { icon: '⚠️', className: 'warning' },
    'contains eggs': { icon: '⚠️', className: 'warning' },
    spicy: { icon: '🌶️', className: 'spicy' },
    'sugar free': { icon: '🍯', className: 'sugarFree' },
    organic: { icon: '🌿', className: 'organic' },
    'low sodium': { icon: '🧂', className: 'lowSodium' },
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

// Export the available allergens for form components
export const AVAILABLE_ALLERGENS = [
  'vegan',
  'vegetarian',
  'gluten_free',
  'dairy_free',
  'nut_free',
  'halal',
  'kosher',
  'contains_nuts',
  'contains_dairy',
  'contains_gluten',
  'contains_soy',
  'contains_eggs',
  'spicy',
  'sugar_free',
  'organic',
  'low_sodium',
];

interface AllergenDisplayProps {
  allergens?: string[];
  id?: string;
  maxVisible?: number;
  showLabel?: boolean;
  variant?: 'compact' | 'full' | 'admin';
  className?: string;
}

export default function AllergenDisplay({
  allergens,
  id = 'allergen-display',
  maxVisible = 3,
  showLabel = true,
  variant = 'full',
  className = '',
}: AllergenDisplayProps) {
  const { t } = useTranslation();

  if (!allergens || allergens.length === 0) {
    // For menu items, preserve space to maintain layout alignment
    if (variant === 'full') {
      return (
        <div className={`${styles.allergensSection} ${className}`} aria-label={t('allergens', 'Allergens')}>
          <div className={styles.allergensLabel} style={{ visibility: 'hidden' }}>
            {t('allergens', 'Allergens')}
          </div>
          <div className={styles.allergensContent} style={{ visibility: 'hidden' }}>
            <span className={styles.allergenTag}>placeholder</span>
          </div>
        </div>
      );
    }

    // For admin or compact views, return null when no allergens
    return null;
  }

  const shown = allergens.slice(0, maxVisible);
  const remaining = allergens.length - shown.length;

  // Different layouts based on variant
  if (variant === 'compact') {
    return (
      <div className={`${styles.allergensContent} ${className}`}>
        {shown.map((allergen, idx) => {
          const { icon, className: allergenClassName } = getAllergenInfo(allergen);
          return (
            <span
              key={`${id}-allergen-${idx}`}
              className={`${styles.allergenTag} ${styles[allergenClassName]}`}
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
            title={`+${remaining} more allergens: ${allergens.slice(maxVisible).join(', ')}`}
          >
            +{remaining}
          </span>
        )}
      </div>
    );
  }

  if (variant === 'admin') {
    return (
      <div className={`${className}`}>
        {showLabel && <div className={styles.allergensLabel}>{t('allergens', 'Allergens')}</div>}
        <div className={styles.allergensContent}>
          {allergens.map((allergen, idx) => {
            const { icon, className: allergenClassName } = getAllergenInfo(allergen);
            return (
              <span
                key={`${id}-allergen-${idx}`}
                className={`${styles.allergenTag} ${styles[allergenClassName]}`}
                title={allergen}
              >
                <span className={styles.allergenIcon}>{icon}</span>
                <span className={styles.allergenText}>{allergen}</span>
              </span>
            );
          })}
        </div>
      </div>
    );
  }

  // Default 'full' variant - preserves layout spacing
  return (
    <div className={`${styles.allergensSection} ${className}`} aria-label={t('allergens', 'Allergens')}>
      {showLabel && <div className={styles.allergensLabel}>{t('allergens', 'Allergens')}</div>}
      <div className={styles.allergensContent}>
        {shown.map((allergen, idx) => {
          const { icon, className: allergenClassName } = getAllergenInfo(allergen);
          return (
            <span
              key={`${id}-allergen-${idx}`}
              className={`${styles.allergenTag} ${styles[allergenClassName]}`}
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
            title={`+${remaining} more allergens: ${allergens.slice(maxVisible).join(', ')}`}
          >
            +{remaining}
          </span>
        )}
      </div>
    </div>
  );
}
