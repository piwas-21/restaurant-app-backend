import React, { useState, useEffect } from 'react';
import styles from './ProductCustomizationInBundle.module.css';
import { DetailedIngredient, SuggestedSideItem } from '@/types/menu';
import { useTranslation } from 'react-i18next';
import type { LanguageCode } from '@/components/LanguageSwitcher';

interface ProductCustomizationInBundleProps {
  isOpen: boolean;
  onClose: () => void;
  productName: string;
  basePrice: number;
  detailedIngredients?: DetailedIngredient[];
  suggestedSideItems?: SuggestedSideItem[];
  onConfirm: (customization: ProductCustomization) => void;
  initialCustomization?: ProductCustomization;
  currentLanguage: LanguageCode;
}

export interface ProductCustomization {
  selectedIngredients: string[];
  excludedIngredients: string[];
  ingredientQuantities: Record<string, number>;
  selectedSideItems: Array<{ id: string; quantity: number }>;
  specialInstructions?: string;
  totalPrice: number;
}

const ProductCustomizationInBundle: React.FC<ProductCustomizationInBundleProps> = ({
  isOpen,
  onClose,
  productName,
  basePrice,
  detailedIngredients = [],
  suggestedSideItems = [],
  onConfirm,
  initialCustomization,
  currentLanguage,
}) => {
  const { t } = useTranslation();

  // Helper function to get translated ingredient name
  const getIngredientName = (ingredient: DetailedIngredient): string => {
    return ingredient.content?.[currentLanguage]?.name ||
           ingredient.content?.en?.name ||
           ingredient.name;
  };

  const [selectedIngredients, setSelectedIngredients] = useState<Set<string>>(new Set());
  const [excludedIngredients, setExcludedIngredients] = useState<Set<string>>(new Set());
  const [ingredientQuantities, setIngredientQuantities] = useState<Record<string, number>>({});
  const [selectedSideItems, setSelectedSideItems] = useState<Map<string, number>>(new Map());
  const [specialInstructions, setSpecialInstructions] = useState('');

  useEffect(() => {
    if (isOpen) {
      // Initialize with default selections or previous customization
      if (initialCustomization) {
        setSelectedIngredients(new Set(initialCustomization.selectedIngredients));
        setExcludedIngredients(new Set(initialCustomization.excludedIngredients));
        setIngredientQuantities(initialCustomization.ingredientQuantities);
        const sidesMap = new Map<string, number>();
        initialCustomization.selectedSideItems.forEach(si => sidesMap.set(si.id, si.quantity));
        setSelectedSideItems(sidesMap);
        setSpecialInstructions(initialCustomization.specialInstructions || '');
      } else {
        // Default: include all non-optional ingredients that are included in base price
        const defaultSelected = new Set<string>();
        const defaultQuantities: Record<string, number> = {};

        detailedIngredients.forEach(ing => {
          // Always select non-optional ingredients
          if (!ing.isOptional) {
            defaultSelected.add(ing.id);
            defaultQuantities[ing.id] = 1;
          }
          // Also select all optional ingredients by default (users can deselect if they don't want)
          else {
            defaultSelected.add(ing.id);
            defaultQuantities[ing.id] = 1;
          }
        });

        setSelectedIngredients(defaultSelected);
        setExcludedIngredients(new Set());
        setIngredientQuantities(defaultQuantities);
        setSelectedSideItems(new Map());
        setSpecialInstructions('');
      }
    }
  }, [isOpen, detailedIngredients, initialCustomization]);

  const calculateTotalPrice = (): number => {
    let total = basePrice;

    // Add ingredient costs
    detailedIngredients.forEach(ing => {
      if (ing.isIncludedInBasePrice) {
        // Ingredient price is included in base price for 1 quantity
        const isSelected = selectedIngredients.has(ing.id);
        const quantity = ingredientQuantities[ing.id] || 1;

        if (!isSelected) {
          // Deselected: deduct the included quantity (1)
          total -= ing.price;
        } else if (quantity > 1) {
          // Selected with more than 1: add extra quantities beyond the free one
          total += ing.price * (quantity - 1);
        }
        // quantity == 1: already in base price, no change
      } else if (selectedIngredients.has(ing.id)) {
        // Regular optional ingredient (not included in base)
        // Add price if user selected it
        const quantity = ingredientQuantities[ing.id] || 1;
        total += ing.price * quantity;
      }
    });

    // Add side item costs
    selectedSideItems.forEach((quantity, sideId) => {
      const side = suggestedSideItems.find(s => s.id === sideId);
      if (side) {
        total += side.sideItemBasePrice * quantity;
      }
    });

    return total;
  };

  const handleIngredientToggle = (ingredient: DetailedIngredient) => {
    const newSelected = new Set(selectedIngredients);
    const newExcluded = new Set(excludedIngredients);
    const newQuantities = { ...ingredientQuantities };

    // Non-optional ingredients cannot be deselected
    if (!ingredient.isOptional) {
      return;
    }

    // Optional ingredient: toggle selection
    if (newSelected.has(ingredient.id)) {
      newSelected.delete(ingredient.id);
      delete newQuantities[ingredient.id];
    } else {
      newSelected.add(ingredient.id);
      newQuantities[ingredient.id] = 1;
    }

    setSelectedIngredients(newSelected);
    setExcludedIngredients(newExcluded);
    setIngredientQuantities(newQuantities);
  };

  const handleIngredientQuantityChange = (ingredientId: string, delta: number) => {
    const ingredient = detailedIngredients.find(i => i.id === ingredientId);
    if (!ingredient) return;

    const currentQty = ingredientQuantities[ingredientId] || 1;
    const newQty = Math.max(1, Math.min(ingredient.maxQuantity, currentQty + delta));

    setIngredientQuantities({
      ...ingredientQuantities,
      [ingredientId]: newQty,
    });
  };

  const handleSideItemToggle = (sideItem: SuggestedSideItem) => {
    const newSides = new Map(selectedSideItems);

    if (newSides.has(sideItem.id)) {
      newSides.delete(sideItem.id);
    } else {
      newSides.set(sideItem.id, 1);
    }

    setSelectedSideItems(newSides);
  };

  const handleSideItemQuantityChange = (sideId: string, delta: number) => {
    const newSides = new Map(selectedSideItems);
    const currentQty = newSides.get(sideId) || 1;
    const newQty = Math.max(1, currentQty + delta);

    newSides.set(sideId, newQty);
    setSelectedSideItems(newSides);
  };

  const handleConfirm = () => {
    const customization: ProductCustomization = {
      selectedIngredients: Array.from(selectedIngredients),
      excludedIngredients: Array.from(excludedIngredients),
      ingredientQuantities,
      selectedSideItems: Array.from(selectedSideItems.entries()).map(([id, quantity]) => ({
        id,
        quantity,
      })),
      specialInstructions: specialInstructions.trim() || undefined,
      totalPrice: calculateTotalPrice(),
    };

    onConfirm(customization);
    onClose();
  };

  if (!isOpen) return null;

  const totalPrice = calculateTotalPrice();
  const hasCustomizableItems = detailedIngredients.length > 0 || suggestedSideItems.length > 0;

  return (
    <div className={styles.modalOverlay} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h3>{productName}</h3>
          <button onClick={onClose} className={styles.closeButton} aria-label={t('close')}>
            ✕
          </button>
        </div>

        <div className={styles.modalBody}>
          <div className={styles.priceDisplay}>
            <span>{t('base_price')}:</span>
            <span className={styles.price}>CHF {basePrice.toFixed(2)}</span>
          </div>

          {!hasCustomizableItems && (
            <p className={styles.noCustomization}>{t('no_customization_available')}</p>
          )}

          {/* Ingredients Section */}
          {detailedIngredients.length > 0 && (
            <div className={styles.section}>
              <h4>{t('customize_ingredients')}</h4>

              {/* Included Ingredients (non-optional) */}
              {detailedIngredients.filter(ing => !ing.isOptional).length > 0 && (
                <div className={styles.ingredientGroup}>
                  <h5 className={styles.groupTitle}>{t('ingredient_included')}</h5>
                  <div className={styles.itemsList}>
                    {detailedIngredients.filter(ing => !ing.isOptional).map(ingredient => {
                      const isSelected = selectedIngredients.has(ingredient.id);
                      const quantity = ingredientQuantities[ingredient.id] || 1;
                      const showQuantity = isSelected && ingredient.maxQuantity > 1;

                      return (
                        <div
                          key={ingredient.id}
                          className={`${styles.customizationItem} ${isSelected ? styles.selected : ''} ${styles.disabled}`}
                        >
                          <label className={styles.itemLabel}>
                            <input
                              type="checkbox"
                              checked={isSelected}
                              onChange={() => handleIngredientToggle(ingredient)}
                              disabled={true}
                              className={styles.checkbox}
                            />
                            <div className={styles.itemInfo}>
                              <span className={styles.itemName}>
                                {getIngredientName(ingredient)}
                              </span>
                              {ingredient.price > 0 && !ingredient.isIncludedInBasePrice && (
                                <span className={styles.itemPrice}>
                                  +CHF {ingredient.price.toFixed(2)}
                                </span>
                              )}
                            </div>
                          </label>

                          {showQuantity && (
                            <div className={styles.quantityControl}>
                              <button
                                onClick={() => handleIngredientQuantityChange(ingredient.id, -1)}
                                disabled={quantity <= 1}
                                className={styles.quantityButton}
                              >
                                −
                              </button>
                              <span className={styles.quantity}>{quantity}</span>
                              <button
                                onClick={() => handleIngredientQuantityChange(ingredient.id, 1)}
                                disabled={quantity >= ingredient.maxQuantity}
                                className={styles.quantityButton}
                              >
                                +
                              </button>
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* Optional Ingredients */}
              {detailedIngredients.filter(ing => ing.isOptional).length > 0 && (
                <div className={styles.ingredientGroup}>
                  <h5 className={styles.groupTitle}>{t('ingredient_optional')}</h5>
                  <div className={styles.itemsList}>
                    {detailedIngredients.filter(ing => ing.isOptional).map(ingredient => {
                      const isSelected = selectedIngredients.has(ingredient.id);
                      const quantity = ingredientQuantities[ingredient.id] || 1;
                      const showQuantity = isSelected && ingredient.maxQuantity > 1;

                      return (
                        <div
                          key={ingredient.id}
                          className={`${styles.customizationItem} ${isSelected ? styles.selected : ''}`}
                        >
                          <label className={styles.itemLabel}>
                            <input
                              type="checkbox"
                              checked={isSelected}
                              onChange={() => handleIngredientToggle(ingredient)}
                              className={styles.checkbox}
                            />
                            <div className={styles.itemInfo}>
                              <span className={styles.itemName}>
                                {getIngredientName(ingredient)}
                              </span>
                              {ingredient.price > 0 && (
                                <span className={styles.itemPrice}>
                                  {ingredient.isIncludedInBasePrice
                                    ? isSelected
                                      ? quantity > 1
                                        ? `+CHF ${(ingredient.price * (quantity - 1)).toFixed(2)}` // Show extra cost when quantity > 1
                                        : "" // Quantity 1 is already in base price
                                      : `-CHF ${ingredient.price.toFixed(2)}` // Deducted when deselected
                                    : `+CHF ${(ingredient.price * quantity).toFixed(2)}`} {/* Added when selected */}
                                </span>
                              )}
                            </div>
                          </label>

                          {showQuantity && (
                            <div className={styles.quantityControl}>
                              <button
                                onClick={() => handleIngredientQuantityChange(ingredient.id, -1)}
                                disabled={quantity <= 1}
                                className={styles.quantityButton}
                              >
                                −
                              </button>
                              <span className={styles.quantity}>{quantity}</span>
                              <button
                                onClick={() => handleIngredientQuantityChange(ingredient.id, 1)}
                                disabled={quantity >= ingredient.maxQuantity}
                                className={styles.quantityButton}
                              >
                                +
                              </button>
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Side Items Section */}
          {suggestedSideItems.length > 0 && (
            <div className={styles.section}>
              <h4>{t('side_items')}</h4>
              <div className={styles.itemsList}>
                {suggestedSideItems.map(sideItem => {
                  const isSelected = selectedSideItems.has(sideItem.id);
                  const quantity = selectedSideItems.get(sideItem.id) || 1;

                  return (
                    <div key={sideItem.id} className={styles.customizationItem}>
                      <label className={styles.itemLabel}>
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={() => handleSideItemToggle(sideItem)}
                          className={styles.checkbox}
                        />
                        <div className={styles.itemInfo}>
                          <span className={styles.itemName}>
                            {sideItem.sideItemProductName}
                            {sideItem.isRequired && (
                              <span className={styles.requiredBadge}>{t('required')}</span>
                            )}
                          </span>
                          <span className={styles.itemPrice}>
                            +CHF {sideItem.sideItemBasePrice.toFixed(2)}
                          </span>
                        </div>
                      </label>

                      {isSelected && (
                        <div className={styles.quantityControl}>
                          <button
                            onClick={() => handleSideItemQuantityChange(sideItem.id, -1)}
                            disabled={quantity <= 1}
                            className={styles.quantityButton}
                          >
                            −
                          </button>
                          <span className={styles.quantity}>{quantity}</span>
                          <button
                            onClick={() => handleSideItemQuantityChange(sideItem.id, 1)}
                            className={styles.quantityButton}
                          >
                            +
                          </button>
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            </div>
          )}

          {/* Special Instructions */}
          <div className={styles.section}>
            <h4>{t('special_instructions')}</h4>
            <textarea
              value={specialInstructions}
              onChange={(e) => setSpecialInstructions(e.target.value)}
              placeholder={t('add_special_instructions')}
              className={styles.textarea}
              rows={3}
            />
          </div>
        </div>

        <div className={styles.modalFooter}>
          <div className={styles.totalPrice}>
            <span>{t('total')}:</span>
            <span className={styles.price}>CHF {totalPrice.toFixed(2)}</span>
          </div>
          <button onClick={handleConfirm} className={styles.confirmButton}>
            {t('confirm')}
          </button>
        </div>
      </div>
    </div>
  );
};

export default ProductCustomizationInBundle;
