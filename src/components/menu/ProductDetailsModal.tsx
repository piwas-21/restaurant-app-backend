"use client";

import React, { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import styles from "./ProductDetailsModal.module.css";
import type { MenuItem as MenuItemType, DetailedProduct } from "@/types/menu";
import { useTranslation } from "react-i18next";
import { getProductById } from "@/services/menuService";
import AllergenDisplay from "@/components/common/AllergenDisplay";
import MenuCustomizationModal from "./MenuCustomizationModal";
import { addItemToBasket } from "@/services/basketService";
import type { LanguageCode } from "@/components/LanguageSwitcher";

type Props = {
  isOpen: boolean;
  item: MenuItemType | null;
  onClose: () => void;
};

export default function ProductDetailsModal({ isOpen, item, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const currentLanguage = (i18n.language.split("-")[0] || "en") as LanguageCode;

  const [detailedProduct, setDetailedProduct] = useState<DetailedProduct | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // State for optional ingredients selection
  const [selectedIngredients, setSelectedIngredients] = useState<Set<string>>(new Set());

  // State for side items selection
  const [selectedSideItems, setSelectedSideItems] = useState<Map<string, number>>(new Map());

  // Fetch detailed product data when modal opens
  useEffect(() => {
    if (!isOpen || !item) {
      setDetailedProduct(null);
      setError(null);
      setSelectedIngredients(new Set());
      setSelectedSideItems(new Map());
      return;
    }

    const fetchProductDetails = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const response = await getProductById(item.id) as { success: boolean; data?: any; message?: string };
        if (response.success && response.data) {
          setDetailedProduct(response.data);

          // Initialize selected ingredients (non-optional ingredients are selected by default)
          const defaultIngredients = new Set<string>();
          if (response.data.detailedIngredients) {
            response.data.detailedIngredients
              .filter((ing: any) => ing.isActive && !ing.isOptional)
              .forEach((ing: any) => defaultIngredients.add(ing.id));
          }
          setSelectedIngredients(defaultIngredients);

          // Initialize selected side items (required side items are selected by default)
          const defaultSideItems = new Map<string, number>();
          if (response.data.suggestedSideItems) {
            response.data.suggestedSideItems
              .filter((side: any) => side.isRequired)
              .forEach((side: any) => defaultSideItems.set(side.id, 1));
          }
          setSelectedSideItems(defaultSideItems);
        } else {
          throw new Error(response.message || "Failed to fetch product details");
        }
      } catch (err: any) {
        console.error("Failed to fetch product details:", err);
        setError(err.message || "Failed to load product details");
        // Fallback to using the item data from the list
        setDetailedProduct(null);
      } finally {
        setIsLoading(false);
      }
    };

    fetchProductDetails();
  }, [isOpen, item]);

  if (!isOpen || !item) return null;

  // Use detailed product data if available, otherwise fallback to menu item data
  const productData = detailedProduct || item;
  const title = detailedProduct
    ? (detailedProduct.content?.[currentLanguage]?.name || detailedProduct.content?.en?.name || detailedProduct.name)
    : (item.content?.[currentLanguage]?.name || item.content?.en?.name || item.name);

  const description = detailedProduct
    ? (detailedProduct.content?.[currentLanguage]?.description || detailedProduct.content?.en?.description || detailedProduct.description || "")
    : (item.content?.[currentLanguage]?.description || item.content?.en?.description || item.longDescription || "");

  const price = detailedProduct?.basePrice || (typeof item.price === 'number' ? item.price : parseFloat(item.price as any));

  // Get ingredients from detailedIngredients with multilingual support
  const getIngredients = () => {
    const productToUse = detailedProduct || item;
    if (productToUse.detailedIngredients && productToUse.detailedIngredients.length > 0) {
      return productToUse.detailedIngredients
        .filter((ing: any) => ing.isActive)
        .map((ing: any) => {
          // Try to get name in current language, fallback to English, then base name
          return ing.content?.[currentLanguage]?.name || ing.content?.en?.name || ing.name;
        });
    }
    // Fallback to legacy ingredients array
    return productToUse.ingredients || [];
  };

  const ingredients = getIngredients();

  // Get optional ingredients for selection
  const getOptionalIngredients = () => {
    if (!detailedProduct?.detailedIngredients) return [];
    return detailedProduct.detailedIngredients.filter((ing: any) => ing.isActive && ing.isOptional);
  };

  const optionalIngredients = getOptionalIngredients();

  // Handler for ingredient toggle
  const toggleIngredient = (ingredientId: string) => {
    const newSelected = new Set(selectedIngredients);
    if (newSelected.has(ingredientId)) {
      newSelected.delete(ingredientId);
    } else {
      newSelected.add(ingredientId);
    }
    setSelectedIngredients(newSelected);
  };

  // Handler for side item toggle
  const toggleSideItem = (sideItemId: string, checked: boolean) => {
    const newSelected = new Map(selectedSideItems);
    if (checked) {
      newSelected.set(sideItemId, 1);
    } else {
      newSelected.delete(sideItemId);
    }
    setSelectedSideItems(newSelected);
  };

  // Handler for side item quantity change
  const updateSideItemQuantity = (sideItemId: string, quantity: number) => {
    const newSelected = new Map(selectedSideItems);
    if (quantity > 0) {
      newSelected.set(sideItemId, quantity);
    } else {
      newSelected.delete(sideItemId);
    }
    setSelectedSideItems(newSelected);
  };

  // Calculate total price
  const calculateTotalPrice = () => {
    let total = price;

    // Add optional ingredient prices
    if (detailedProduct?.detailedIngredients) {
      detailedProduct.detailedIngredients.forEach((ing: any) => {
        if (ing.isOptional && selectedIngredients.has(ing.id)) {
          total += ing.price || 0;
        }
      });
    }

    // Add side item prices
    if (detailedProduct?.suggestedSideItems) {
      detailedProduct.suggestedSideItems.forEach((side: any) => {
        const quantity = selectedSideItems.get(side.id);
        if (quantity) {
          total += side.price * quantity;
        }
      });
    }

    return total;
  };

  const totalPrice = calculateTotalPrice();
  const hasCustomizations = optionalIngredients.length > 0 || (detailedProduct?.suggestedSideItems && detailedProduct.suggestedSideItems.length > 0);

  // Check if this is a menu bundle product
  const isMenuProduct = detailedProduct?.type === 'menu' && detailedProduct?.menuDefinition;

  // If it's a menu product, show the MenuCustomizationModal instead
  if (isMenuProduct && detailedProduct) {
    return (
      <MenuCustomizationModal
        isOpen={isOpen}
        onClose={onClose}
        productId={detailedProduct.id}
        productName={title}
        basePrice={price}
        menuDefinition={detailedProduct.menuDefinition!}
        onAddToBasket={async (selectedOptions, totalPrice) => {
          try {
            await addItemToBasket({
              productId: detailedProduct.id,
              quantity: 1,
              selectedMenuOptions: selectedOptions,
            });
            onClose();
          } catch (error) {
            console.error('Error adding menu to basket:', error);
          }
        }}
        currentLanguage={currentLanguage}
      />
    );
  }

  // Regular product modal
  return createPortal(
    <div className={styles.productDetailsModal} onClick={onClose}>
      <div className={styles.productDetailsContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.productDetailsHeader}>
          <h3>{title}</h3>
          <button className={styles.productDetailsClose} onClick={onClose} aria-label={t('close')}>×</button>
        </div>
        <div className={styles.productDetailsBody}>
          {isLoading && (
            <div className={styles.productDetailSection}>
              <p>{t('loading', 'Loading detailed information...')}</p>
            </div>
          )}

          {error && (
            <div className={styles.productDetailSection}>
              <p className={styles.errorMessage}>{error}</p>
            </div>
          )}

          {description && (
            <div className={styles.productDetailSection}>
              <h4>{t('description', 'Description')}</h4>
              <p>{description}</p>
            </div>
          )}

          {ingredients.length > 0 && (
            <div className={styles.productDetailSection}>
              <h4>{t('ingredients')}:</h4>
              <div className={styles.allergyTags}>
                {ingredients.map((ingredient, idx) => (
                  <span key={`${item.id}-ing-full-${idx}`} className={styles.allergyTag}>{ingredient}</span>
                ))}
              </div>
            </div>
          )}

          <div className={styles.productDetailSection} style={{ justifyItems: 'flex-start' }}>
            <h4>{t('allergens', 'Allergens')}:</h4>
            <AllergenDisplay
              allergens={detailedProduct?.allergens || item.allergens}
              id={`${item.id}-modal`}
              variant="admin"
              showLabel={false}
            />
          </div>

          {((detailedProduct?.preparationTimeMinutes ?? 0) > 0 || (item.preparationTimeMinutes ?? 0) > 0) && (
            <div className={styles.productDetailSection}>
              <h4>{t('preparation_time', 'Preparation Time')}:</h4>
              <p className={styles.preparationTime}>
                <span className={styles.timeIcon}>⏱️</span>
                {detailedProduct?.preparationTimeMinutes || item.preparationTimeMinutes} {t('minutes', 'minutes')}
              </p>
            </div>
          )}

          {detailedProduct?.variations && detailedProduct.variations.length > 0 && (
            <div className={styles.productDetailSection}>
              <h4>{t('variations', 'Variations')}:</h4>
              <div className={styles.variationsList}>
                {detailedProduct.variations
                  .filter(v => v.isActive)
                  .sort((a, b) => a.displayOrder - b.displayOrder)
                  .map((variation, idx) => {
                    // Try to get name/description in current language
                    const varName = variation.content?.[currentLanguage]?.name || variation.content?.en?.name || variation.name;
                    const varDesc = variation.content?.[currentLanguage]?.description || variation.content?.en?.description || variation.description;

                    return (
                      <div key={`${item.id}-variation-${idx}`} className={styles.variationItem}>
                        <span className={styles.variationName}>{varName}</span>
                        <span className={styles.variationPrice}>
                          CHF {variation.finalPrice.toFixed(2)}
                          {variation.priceModifier !== 0 && (
                            <span className={styles.priceModifier}>
                              {variation.priceModifier > 0
                                ? ` (+${variation.priceModifier.toFixed(2)})`
                                : ` (${variation.priceModifier.toFixed(2)})`
                              }
                            </span>
                          )}
                        </span>
                        {varDesc && (
                          <p className={styles.variationDescription}>{varDesc}</p>
                        )}
                      </div>
                    );
                  })}
              </div>
            </div>
          )}

          {detailedProduct?.suggestedSideItems && detailedProduct.suggestedSideItems.length > 0 && (
            <div className={styles.productDetailSection}>
              <h4>{t('suggested_sides', 'Suggested Side Items')}:</h4>
              <div className={styles.sideItemsList}>
                {detailedProduct.suggestedSideItems
                  .sort((a, b) => a.displayOrder - b.displayOrder)
                  .map((sideItem, idx) => (
                    <div key={`${item.id}-side-${idx}`} className={styles.sideItemCard}>
                      <div className={styles.sideItemInfo}>
                        <span className={styles.sideItemName}>
                          {sideItem.name}
                          {sideItem.isRequired && <span className={styles.requiredBadge}>{t('required', '*')}</span>}
                        </span>
                        <span className={styles.sideItemPrice}>CHF {sideItem.price.toFixed(2)}</span>
                      </div>
                      {sideItem.description && (
                        <p className={styles.sideItemDescription}>{sideItem.description}</p>
                      )}
                    </div>
                  ))}
              </div>
            </div>
          )}

          <div className={styles.productDetailSection}>
            <p className={styles.itemPrice}>CHF {price.toFixed(2)}</p>
          </div>
        </div>
      </div>
    </div>,
    document.body
  );
}
