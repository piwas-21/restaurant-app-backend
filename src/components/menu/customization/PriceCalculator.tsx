"use client";

import React from "react";
import { useTranslation } from "react-i18next";
import { Plus, Minus } from "lucide-react";
import type { ProductIngredient, SuggestedSideItem } from "@/types/menu";
import styles from "./PriceCalculator.module.css";

interface PriceCalculatorProps {
  basePrice: number;
  ingredients: ProductIngredient[];
  selectedIngredients: string[];
  sideItems: SuggestedSideItem[];
  selectedSideItems: Array<{ id: string; quantity: number }>;
  quantity: number;
  onQuantityChange: (delta: number) => void;
  selectedVariationId?: string | null;
  variations?: Array<{
    id?: string;
    name: string;
    priceModifier: number;
  }>;
}

export default function PriceCalculator({
  basePrice,
  ingredients,
  selectedIngredients,
  sideItems,
  selectedSideItems,
  quantity,
  onQuantityChange,
  selectedVariationId,
  variations,
}: PriceCalculatorProps) {
  const { t } = useTranslation();

    // Calculate base price with variation modifier (additive)
  let adjustedBasePrice = basePrice;
  if (selectedVariationId && variations && variations.length > 0) {
    const selectedVariation = variations.find(v => v.id === selectedVariationId);
    if (selectedVariation) {
      // priceModifier is always additive: basePrice + modifier
      adjustedBasePrice = basePrice + selectedVariation.priceModifier;
    }
  }

  // Calculate ingredient customization cost
  const ingredientsCost = selectedIngredients.reduce((total, ingredientId) => {
    const ingredient = ingredients.find((i) => i.id === ingredientId && i.isOptional);
    return total + (ingredient?.price || 0);
  }, 0);

  // Calculate side items cost
  const sideItemsCost = selectedSideItems.reduce((total, selectedItem) => {
    const sideItem = sideItems.find((item) => item.id === selectedItem.id);
    return total + (sideItem?.price || 0) * selectedItem.quantity;
  }, 0);

  // Calculate subtotal (before quantity) using adjusted base price
  const subtotal = adjustedBasePrice + ingredientsCost + sideItemsCost;

  // Calculate total (with quantity)
  const total = subtotal * quantity;

  // Get names of selected optional ingredients
  const optionalIngredientNames = selectedIngredients
    .map((id) => {
      const ing = ingredients.find((i) => i.id === id && i.isOptional && i.price > 0);
      return ing;
    })
    .filter(Boolean);

  return (
    <div className={styles.priceCalculator}>
      {/* Price Breakdown - Compact */}
      <div className={styles.priceBreakdown}>
        <div className={styles.priceRow}>
          <span className={styles.priceLabel}>{t("base_price")}:</span>
          <span className={styles.priceValue}>CHF {adjustedBasePrice.toFixed(2)}</span>
        </div>

        {/* Show customization details if any */}
        {ingredientsCost > 0 && (
          <div className={styles.priceRow}>
            <span className={styles.priceLabel}>
              {t("customization_cost")}:
              {optionalIngredientNames.length > 0 && (
                <span className={styles.itemDetails}>
                  {optionalIngredientNames.map((ing, idx) => (
                    <span key={ing!.id}>
                      {idx > 0 && ", "}
                      {ing!.name}
                    </span>
                  ))}
                </span>
              )}
            </span>
            <span className={styles.priceValue}>CHF {ingredientsCost.toFixed(2)}</span>
          </div>
        )}

        {/* Show side items */}
        {selectedSideItems.length > 0 &&
          selectedSideItems.map((selectedItem) => {
            const sideItem = sideItems.find((item) => item.id === selectedItem.id);
            if (!sideItem) return null;

            return (
              <div key={selectedItem.id} className={styles.priceRow}>
                <span className={styles.priceLabel}>
                  {sideItem.name} × {selectedItem.quantity}:
                </span>
                <span className={styles.priceValue}>
                  CHF {(sideItem.price * selectedItem.quantity).toFixed(2)}
                </span>
              </div>
            );
          })}

        {/* Divider if there are customizations */}
        {(ingredientsCost > 0 || selectedSideItems.length > 0) && (
          <div className={styles.divider} />
        )}

        {/* Subtotal */}
        {quantity > 1 && (
          <div className={styles.priceRow}>
            <span className={styles.priceLabel}>{t("subtotal")}:</span>
            <span className={styles.priceValue}>CHF {subtotal.toFixed(2)}</span>
          </div>
        )}
      </div>

      {/* Bottom Row: Quantity + Total (Inline) */}
      <div className={styles.footerRow}>
        {/* Quantity Selector */}
        <div className={styles.quantitySelector}>
          <span className={styles.quantityLabel}>{t("quantity")}:</span>
          <div className={styles.quantityControls}>
            <button
              onClick={() => onQuantityChange(-1)}
              disabled={quantity <= 1}
              className={styles.quantityButton}
              aria-label={t("decrease_quantity")}
              type="button"
            >
              <Minus size={18} />
            </button>
            <span className={styles.quantityValue}>{quantity}</span>
            <button
              onClick={() => onQuantityChange(1)}
              disabled={quantity >= 99}
              className={styles.quantityButton}
              aria-label={t("increase_quantity")}
              type="button"
            >
              <Plus size={18} />
            </button>
          </div>
        </div>

        {/* Total */}
        <div className={styles.totalPrice}>
          <span className={styles.totalLabel}>{t("total_price")}:</span>
          <span className={styles.totalValue}>CHF {total.toFixed(2)}</span>
        </div>
      </div>
    </div>
  );
}
