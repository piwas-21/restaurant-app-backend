"use client";

import React from "react";
import { useTranslation } from "react-i18next";
import styles from "./VariationsSection.module.css";

interface Variation {
  id?: string;
  name: string;
  description?: string;
  priceModifier: number;
  isActive: boolean;
  displayOrder: number;
  content?: Record<string, {
    name: string;
    description?: string;
  }>;
}

interface VariationsSectionProps {
  variations: Variation[];
  selectedVariationId: string | null;
  onVariationChange: (variationId: string | null) => void;
  basePrice: number;
  currentLanguage: string;
  productName: string;
}

export default function VariationsSection({
  variations,
  selectedVariationId,
  onVariationChange,
  basePrice,
  currentLanguage,
  productName,
}: VariationsSectionProps) {
  const { t } = useTranslation();

  // Filter only active variations and sort by display order
  const activeVariations = variations
    .filter((v) => v.isActive)
    .sort((a, b) => a.displayOrder - b.displayOrder);

  if (activeVariations.length === 0) {
    return null;
  }

  const getVariationContent = (variation: Variation) => {
    const content = variation.content?.[currentLanguage] || variation.content?.en;
    return {
      name: content?.name || variation.name,
      description: content?.description || variation.description
    };
  };

  return (
    <div className={styles.section}>
      <h3 className={styles.sectionTitle}>{t("select_variation")}</h3>
      <div className={styles.variationsList}>
        {/* Default/No variation option */}
        <label className={styles.variationOption}>
          <input
            type="radio"
            name="variation"
            checked={selectedVariationId === null}
            onChange={() => onVariationChange(null)}
            className={styles.variationRadio}
          />
          <div className={styles.variationContent}>
            <div className={styles.variationInfo}>
              <span className={styles.variationName}>{productName}</span>
              {basePrice > 0 && (
                <span className={styles.variationPrice}>
                  CHF {basePrice.toFixed(2)}
                </span>
              )}
            </div>
          </div>
        </label>

        {/* Variation options */}
        {activeVariations.map((variation) => {
          const { name, description } = getVariationContent(variation);

          // priceModifier is always additive (positive = add, negative = subtract)
          const varPrice = basePrice + variation.priceModifier;
          const priceChangeText = variation.priceModifier >= 0
            ? ` +${variation.priceModifier.toFixed(2)} CHF`
            : ` ${variation.priceModifier.toFixed(2)} CHF`;

          const variationId = variation.id || variation.name;

          return (
            <label key={variationId} className={styles.variationOption}>
              <input
                type="radio"
                name="variation"
                checked={selectedVariationId === variationId}
                onChange={() => onVariationChange(variationId)}
                className={styles.variationRadio}
              />
              <div className={styles.variationContent}>
                <div className={styles.variationInfo}>
                  <span className={styles.variationName}>{name}</span>
                  {description && (
                    <span className={styles.variationDescription}>
                      {description}
                    </span>
                  )}
                </div>
                {varPrice > 0 && (
                  <span className={styles.variationPrice}>
                    CHF {varPrice.toFixed(2)}
                    {priceChangeText && (
                      <span className={styles.priceModifier}>
                        {priceChangeText}
                      </span>
                    )}
                  </span>
                )}
              </div>
            </label>
          );
        })}
      </div>
    </div>
  );
}
