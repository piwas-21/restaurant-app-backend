'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import { Star, Clock } from 'lucide-react';
import Image from 'next/image';
import styles from './FeaturedSpecial.module.css';
import AllergenDisplay from '@/components/common/AllergenDisplay';
import type { FeaturedSpecial as FeaturedSpecialType } from '@/types/menu';

interface FeaturedSpecialProps {
  special: FeaturedSpecialType;
  onAddToCart?: () => void;
  onViewDetails?: () => void;
}

const FeaturedSpecial: React.FC<FeaturedSpecialProps> = ({ special, onAddToCart, onViewDetails }) => {
  const { t, i18n } = useTranslation();
  const currentLanguage = i18n.language || 'en';

  if (!special) {
    return null;
  }

  // Get ingredients from detailedIngredients with multilingual support
  const getIngredients = () => {
    if (special.detailedIngredients && special.detailedIngredients.length > 0) {
      return special.detailedIngredients
        .filter((ing: any) => ing.isActive)
        .map((ing: any) => {
          return ing.content?.[currentLanguage]?.name || ing.content?.en?.name || ing.name;
        });
    }
    // Fallback to legacy ingredients array
    return special.ingredients || [];
  };

  const ingredientsList = getIngredients();

  return (
    <section className={styles.featuredSpecialSection} aria-labelledby="featured-special-heading">
      <div className={styles.featuredSpecialContainer}>
        <div className={styles.featuredSpecialBadge}>
          <Star size={20} fill="gold" color="gold" />
          <span>{t('chefs_special', "Chef's Special")}</span>
        </div>

        <div className={styles.featuredSpecialContent}>
          {special.imageUrl && (
            <div className={styles.featuredSpecialImageContainer}>
              <Image
                src={special.imageUrl}
                alt={special.name}
                width={400}
                height={300}
                style={{ objectFit: 'cover' }}
                className={styles.featuredSpecialImage}
              />
            </div>
          )}

          <div className={styles.featuredSpecialDetails}>
            <h2 id="featured-special-heading" className={styles.featuredSpecialTitle}>
              {special.name}
            </h2>

            {special.description && (
              <p className={styles.featuredSpecialDescription}>{special.description}</p>
            )}

            <div className={styles.featuredSpecialMeta}>
              <div className={styles.featuredSpecialPrice}>
                <span className={styles.priceLabel}>{t('price', 'Price')}:</span>
                <span className={styles.priceValue}>CHF {special.basePrice.toFixed(2)}</span>
              </div>

              {special.preparationTimeMinutes && special.preparationTimeMinutes > 0 && (
                <div className={styles.featuredSpecialTime}>
                  <Clock size={16} />
                  <span>{special.preparationTimeMinutes} {t('minutes', 'min')}</span>
                </div>
              )}
            </div>

            {ingredientsList && ingredientsList.length > 0 && (
              <div className={styles.featuredSpecialIngredients}>
                <strong>{t('ingredients', 'Ingredients')}:</strong>{' '}
                <span>{ingredientsList.join(', ')}</span>
              </div>
            )}

            {special.allergens && special.allergens.length > 0 && (
              <div className={styles.featuredSpecialAllergens}>
                <AllergenDisplay
                  allergens={special.allergens}
                  id={`featured-special-${special.id}`}
                  maxVisible={10}
                  showLabel={true}
                  variant="admin"
                  className={styles.allergenContainer}
                />
              </div>
            )}

            <div className={styles.featuredSpecialActions}>
              {onAddToCart && (
                <button
                  className={styles.featuredSpecialAddButton}
                  onClick={onAddToCart}
                  aria-label={t('add_to_order', 'Add to Order')}
                >
                  {t('add_to_order', 'Add to Order')}
                </button>
              )}
              {onViewDetails && (
                <button
                  className={styles.featuredSpecialDetailsButton}
                  onClick={onViewDetails}
                  aria-label={t('view_details', 'View Details')}
                >
                  {t('details', 'Details')}
                </button>
              )}
            </div>
          </div>
        </div>
      </div>
    </section>
  );
};

export default FeaturedSpecial;
