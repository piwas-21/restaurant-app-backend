"use client";

import React from "react";
import { useTranslation } from "react-i18next";
import type { ProductIngredient } from "@/types/menu";
import styles from "./OptionalIngredientsSection.module.css";

interface OptionalIngredientsSectionProps {
  ingredients: ProductIngredient[];
  selectedIngredients: string[];
  onSelectionChange: (selected: string[]) => void;
  currentLanguage: string;
}

export default function OptionalIngredientsSection({
  ingredients,
  selectedIngredients,
  onSelectionChange,
  currentLanguage,
}: OptionalIngredientsSectionProps) {
  const { t } = useTranslation();

  // Filter active ingredients
  const activeIngredients = ingredients.filter((ing) => ing.isActive);

  // Separate optional and default ingredients
  const defaultIngredients = activeIngredients.filter((ing) => !ing.isOptional);
  const optionalIngredients = activeIngredients.filter((ing) => ing.isOptional);

  if (activeIngredients.length === 0) {
    return null;
  }

  const handleToggle = (ingredientId: string, isOptional: boolean) => {
    // Non-optional ingredients cannot be deselected
    if (!isOptional) {
      return;
    }

    if (selectedIngredients.includes(ingredientId)) {
      onSelectionChange(selectedIngredients.filter((id) => id !== ingredientId));
    } else {
      onSelectionChange([...selectedIngredients, ingredientId]);
    }
  };

  // Get ingredient name in current language
  const getIngredientName = (ingredient: ProductIngredient) => {
    return (
      ingredient.content?.[currentLanguage]?.name ||
      ingredient.content?.en?.name ||
      ingredient.name
    );
  };

  return (
    <div className={styles.section}>
      <h3 className={styles.sectionTitle}>{t("customize_ingredients")}</h3>

      {/* Default Ingredients (always included, can be excluded) */}
      {defaultIngredients.length > 0 && (
        <div className={styles.ingredientGroup}>
          <h4 className={styles.groupTitle}>{t("ingredient_included")}</h4>
          <div className={styles.ingredientList}>
            {defaultIngredients.map((ingredient) => (
              <label key={ingredient.id} className={styles.ingredientItem}>
                <input
                  type="checkbox"
                  checked={selectedIngredients.includes(ingredient.id)}
                  onChange={() => handleToggle(ingredient.id, ingredient.isOptional)}
                  disabled={!ingredient.isOptional}
                  className={styles.checkbox}
                />
                <span className={styles.ingredientName}>{getIngredientName(ingredient)}</span>
                {ingredient.price > 0 && (
                  <span className={styles.ingredientPrice}>
                    {t("ingredient_price", { price: ingredient.price.toFixed(2) })}
                  </span>
                )}
              </label>
            ))}
          </div>
        </div>
      )}

      {/* Optional Ingredients (can be added) */}
      {optionalIngredients.length > 0 && (
        <div className={styles.ingredientGroup}>
          <h4 className={styles.groupTitle}>{t("ingredient_optional")}</h4>
          <div className={styles.ingredientList}>
            {optionalIngredients.map((ingredient) => (
              <label key={ingredient.id} className={styles.ingredientItem}>
                <input
                  type="checkbox"
                  checked={selectedIngredients.includes(ingredient.id)}
                  onChange={() => handleToggle(ingredient.id, ingredient.isOptional)}
                  className={styles.checkbox}
                />
                <span className={styles.ingredientName}>{getIngredientName(ingredient)}</span>
                {ingredient.price > 0 && (
                  <span className={styles.ingredientPrice}>
                    +CHF {ingredient.price.toFixed(2)}
                  </span>
                )}
              </label>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
