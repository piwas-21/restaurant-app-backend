"use client";

import React, { useState, useEffect } from "react";
import { useTranslation } from "react-i18next";
import { Plus, Trash2, GripVertical } from "lucide-react";
import type { ProductIngredient } from "@/types/menu";
import styles from "./ProductIngredientsManager.module.css";

interface ProductIngredientsManagerProps {
  ingredients: ProductIngredient[];
  onChange: (ingredients: ProductIngredient[]) => void;
  productBasePrice: number;
}

export function ProductIngredientsManager({
  ingredients,
  onChange,
  productBasePrice,
}: ProductIngredientsManagerProps) {
  const { t } = useTranslation();
  // Local state to preserve string value during typing
  const [priceInputs, setPriceInputs] = useState<Record<number, string>>({});

  // Initialize price inputs when ingredients change (e.g., loading existing product)
  useEffect(() => {
    const newInputs: Record<number, string> = {};
    ingredients.forEach((ing, idx) => {
      if (priceInputs[idx] === undefined) {
        newInputs[idx] = ing.price === 0 ? '' : String(ing.price).replace('.', ',');
      }
    });
    if (Object.keys(newInputs).length > 0) {
      setPriceInputs(prev => ({ ...prev, ...newInputs }));
    }
    // Only run when ingredients array length changes (new ingredients added/removed)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ingredients.length]);

  const handleAddIngredient = () => {
    // Generate a temporary unique ID for new ingredients (will be replaced by server)
    const tempId = `temp-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
    const newIngredient: ProductIngredient = {
      id: tempId,
      name: "",
      isOptional: false,
      price: 0,
      isActive: true,
      displayOrder: ingredients.length,
      content: {
        en: { name: "", description: "" },
        tr: { name: "", description: "" },
        de: { name: "", description: "" },
        fr: { name: "", description: "" },
        it: { name: "", description: "" },
        ar: { name: "", description: "" },
        es: { name: "", description: "" },
      },
    };
    onChange([...ingredients, newIngredient]);
  };

  const handleRemoveIngredient = (index: number) => {
    const updated = ingredients.filter((_, i) => i !== index);
    onChange(updated);
  };

  const handleIngredientChange = (
    index: number,
    field: keyof ProductIngredient,
    value: any
  ) => {
    const updated = [...ingredients];
    updated[index] = { ...updated[index], [field]: value };
    onChange(updated);
  };

  const handleContentChange = (
    index: number,
    language: string,
    field: "name" | "description",
    value: string
  ) => {
    const updated = [...ingredients];
    if (!updated[index].content) {
      updated[index].content = {};
    }
    if (!updated[index].content![language]) {
      updated[index].content![language] = { name: "", description: "" };
    }
    updated[index].content![language][field] = value;
    onChange(updated);
  };

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <h3 className={styles.title}>{t("product_ingredients")}</h3>
        <button
          type="button"
          onClick={handleAddIngredient}
          className={styles.addButton}
        >
          <Plus size={16} />
          {t("add_ingredient")}
        </button>
      </div>

      <p className={styles.description}>
        {t("ingredients_manager_description")}
      </p>

      {ingredients.length === 0 ? (
        <div className={styles.emptyState}>
          <p>{t("no_ingredients_added")}</p>
        </div>
      ) : (
        <div className={styles.ingredientsList}>
          {ingredients.map((ingredient, index) => (
            <div key={ingredient.id} className={styles.ingredientCard}>
              <div className={styles.ingredientHeader}>
                <GripVertical size={20} className={styles.dragHandle} />
                <div className={styles.ingredientMeta}>
                  <span className={styles.ingredientNumber}>
                    {index + 1}
                  </span>
                  <input
                    type="text"
                    value={ingredient.name}
                    onChange={(e) =>
                      handleIngredientChange(index, "name", e.target.value)
                    }
                    placeholder={t("ingredient_name_placeholder")}
                    className={styles.ingredientNameInput}
                  />
                </div>
                <button
                  type="button"
                  onClick={() => handleRemoveIngredient(index)}
                  className={styles.removeButton}
                  aria-label={t("remove_ingredient")}
                >
                  <Trash2 size={16} />
                </button>
              </div>

              <div className={styles.ingredientFields}>
                <div className={styles.fieldRow}>
                  <label className={styles.checkbox}>
                    <input
                      type="checkbox"
                      checked={ingredient.isOptional}
                      onChange={(e) =>
                        handleIngredientChange(
                          index,
                          "isOptional",
                          e.target.checked
                        )
                      }
                    />
                    <span>{t("ingredient_is_optional")}</span>
                  </label>

                  <label className={styles.checkbox}>
                    <input
                      type="checkbox"
                      checked={ingredient.isActive}
                      onChange={(e) =>
                        handleIngredientChange(
                          index,
                          "isActive",
                          e.target.checked
                        )
                      }
                    />
                    <span>{t("ingredient_is_active")}</span>
                  </label>
                </div>

                {ingredient.isOptional && (
                  <div className={styles.priceField}>
                    <label>
                      {t("additional_price")}
                      <input
                        type="text"
                        value={priceInputs[index] ?? (ingredient.price === 0 ? '' : String(ingredient.price).replace('.', ','))}
                        onChange={(e) => {
                          const value = e.target.value;

                          // Update local input state immediately to preserve typing
                          setPriceInputs(prev => ({ ...prev, [index]: value }));

                          // Allow empty string
                          if (value === '') {
                            handleIngredientChange(index, "price", 0);
                            return;
                          }

                          // Replace dot with comma for decimal separator
                          const normalizedValue = value.replace('.', ',');

                          // Validate format: digits, optional comma, optional digits
                          if (/^-?\d*,?\d*$/.test(normalizedValue)) {
                            // Parse to number
                            const numValue = parseFloat(normalizedValue.replace(',', '.'));
                            if (!isNaN(numValue)) {
                              handleIngredientChange(index, "price", numValue);
                            }
                          }
                        }}
                        onBlur={(e) => {
                          // On blur, ensure we have a valid number and clean up display
                          const value = e.target.value.replace(',', '.');
                          const numValue = parseFloat(value);
                          if (isNaN(numValue) || numValue < 0) {
                            handleIngredientChange(index, "price", 0);
                            setPriceInputs(prev => ({ ...prev, [index]: '' }));
                          } else {
                            handleIngredientChange(index, "price", numValue);
                            setPriceInputs(prev => ({ ...prev, [index]: String(numValue).replace('.', ',') }));
                          }
                        }}
                        placeholder="0,00"
                        className={styles.priceInput}
                      />
                      <span className={styles.currency}>CHF</span>
                    </label>
                    <span className={styles.priceHint}>
                      {t("use_comma_for_decimals")}
                    </span>
                    {ingredient.price > 0 && (
                      <span className={styles.pricePreview}>
                        {t("customer_pays")}:{" "}
                        CHF {(Number(productBasePrice || 0) + (Number(ingredient.price) || 0)).toFixed(2)}
                      </span>
                    )}
                  </div>
                )}

                <details className={styles.translations}>
                  <summary className={styles.translationsSummary}>
                    {t("multilingual_names")}
                  </summary>
                  <div className={styles.translationsGrid}>
                    {["en", "tr", "de", "fr", "it", "ar", "es"].map((lang) => (
                      <div key={lang} className={styles.translationField}>
                        <label>
                          {t(`language_${lang}`)}
                          <input
                            type="text"
                            value={ingredient.content?.[lang]?.name || ""}
                            onChange={(e) =>
                              handleContentChange(
                                index,
                                lang,
                                "name",
                                e.target.value
                              )
                            }
                            placeholder={t("ingredient_name_in_language", {
                              language: t(`language_${lang}`),
                            })}
                            className={styles.translationInput}
                          />
                        </label>
                      </div>
                    ))}
                  </div>
                </details>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
