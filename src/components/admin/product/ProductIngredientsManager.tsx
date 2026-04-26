"use client";

import React, { useState, useEffect, useCallback, useRef } from "react";
import { useTranslation } from "react-i18next";
import { Plus, Trash2, GripVertical } from "lucide-react";
import type { ProductIngredient } from "@/types/menu";
import styles from "./ProductIngredientsManager.module.css";
import { LANGUAGE_CODES } from "@/config/languageConfig";

interface GlobalIngredientSuggestion {
  id: string;
  defaultName: string;
  translations: { languageCode: string; name: string }[];
}

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
  const [suggestions, setSuggestions] = useState<Record<number, GlobalIngredientSuggestion[]>>({});
  const [showSuggestions, setShowSuggestions] = useState<Record<number, boolean>>({});
  const [loadingSuggestions, setLoadingSuggestions] = useState<Record<number, boolean>>({});
  const searchTimeouts = useRef<Record<number, NodeJS.Timeout>>({});

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
      maxQuantity: 1,
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

  const searchGlobalIngredients = useCallback(async (query: string, index: number) => {
    if (!query || query.length < 2) {
      setSuggestions(prev => ({ ...prev, [index]: [] }));
      setShowSuggestions(prev => ({ ...prev, [index]: false }));
      return;
    }

    setLoadingSuggestions(prev => ({ ...prev, [index]: true }));

    try {
      const apiUrl = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5113";
      const response = await fetch(`${apiUrl}/api/global-ingredients/search?query=${encodeURIComponent(query)}&limit=5`);

      if (response.ok) {
        const result = await response.json();
        const items = result.data || [];
        setSuggestions(prev => ({ ...prev, [index]: items }));
        setShowSuggestions(prev => ({ ...prev, [index]: true }));
      }
    } catch (error) {
      console.error("Failed to fetch ingredient suggestions:", error);
    } finally {
      setLoadingSuggestions(prev => ({ ...prev, [index]: false }));
    }
  }, []);

  const handleIngredientNameChange = (index: number, value: string) => {
    // Update ingredient name immediately
    handleIngredientChange(index, "name", value);

    // Clear existing timeout
    if (searchTimeouts.current[index]) {
      clearTimeout(searchTimeouts.current[index]);
    }

    // Debounce search
    searchTimeouts.current[index] = setTimeout(() => {
      searchGlobalIngredients(value, index);
    }, 300);
  };

  const selectGlobalIngredient = (index: number, suggestion: GlobalIngredientSuggestion) => {
    const updated = [...ingredients];

    // Set the ingredient name
    updated[index].name = suggestion.defaultName;

    // Store the global ingredient ID (add this field if it doesn't exist)
    (updated[index] as any).globalIngredientId = suggestion.id;

    // Prefill all translation fields
    if (!updated[index].content) {
      updated[index].content = {};
    }

    suggestion.translations.forEach(translation => {
      const lang = translation.languageCode;
      if (!updated[index].content![lang]) {
        updated[index].content![lang] = { name: "", description: "" };
      }
      updated[index].content![lang].name = translation.name;
    });

    onChange(updated);

    // Hide suggestions
    setShowSuggestions(prev => ({ ...prev, [index]: false }));
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
                <div className={styles.ingredientMeta} style={{ position: 'relative', flex: 1 }}>
                  <span className={styles.ingredientNumber}>
                    {index + 1}
                  </span>
                  <input
                    type="text"
                    value={ingredient.name}
                    onChange={(e) => handleIngredientNameChange(index, e.target.value)}
                    onFocus={() => {
                      if (suggestions[index]?.length > 0) {
                        setShowSuggestions(prev => ({ ...prev, [index]: true }));
                      }
                    }}
                    onBlur={() => {
                      // Delay to allow click on suggestion
                      setTimeout(() => setShowSuggestions(prev => ({ ...prev, [index]: false })), 200);
                    }}
                    placeholder={t("ingredient_name_placeholder")}
                    className={styles.ingredientNameInput}
                  />
                  {loadingSuggestions[index] && (
                    <div style={{ position: 'absolute', right: '10px', top: '50%', transform: 'translateY(-50%)' }}>
                      <span style={{ color: '#666', fontSize: '12px' }}>...</span>
                    </div>
                  )}
                  {showSuggestions[index] && suggestions[index]?.length > 0 && (
                    <div className={styles.suggestions}>
                      {suggestions[index].map((suggestion) => (
                        <div
                          key={suggestion.id}
                          className={styles.suggestionItem}
                          onClick={() => selectGlobalIngredient(index, suggestion)}
                          onMouseDown={(e) => e.preventDefault()} // Prevent blur
                        >
                          <span>{suggestion.defaultName}</span>
                          <span className={styles.suggestionHint}>
                            ({suggestion.translations.length} languages)
                          </span>
                        </div>
                      ))}
                    </div>
                  )}
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

                  {ingredient.isOptional && (
                    <label className={styles.numberInputLabel}>
                      <span>{t("max_quantity")}</span>
                      <input
                        type="number"
                        min="1"
                        value={ingredient.maxQuantity || 1}
                        onChange={(e) =>
                          handleIngredientChange(
                            index,
                            "maxQuantity",
                            parseInt(e.target.value) || 1
                          )
                        }
                        className={styles.numberInput}
                      />
                    </label>
                  )}

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

                    <label className={styles.checkbox} style={{ marginTop: '12px' }}>
                      <input
                        type="checkbox"
                        checked={ingredient.isIncludedInBasePrice || false}
                        onChange={(e) =>
                          handleIngredientChange(
                            index,
                            "isIncludedInBasePrice",
                            e.target.checked
                          )
                        }
                      />
                      <span>{t("ingredient_included_in_base_price")}</span>
                    </label>
                    <span className={styles.priceHint}>
                      {t("ingredient_included_in_base_price_hint")}
                    </span>
                  </div>
                )}

                <details className={styles.translations}>
                  <summary className={styles.translationsSummary}>
                    {t("multilingual_names")}
                  </summary>
                  <div className={styles.translationsGrid}>
                    {LANGUAGE_CODES.map((lang) => (
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
