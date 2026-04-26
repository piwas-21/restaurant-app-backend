import React, { useState, useMemo, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { getProductById } from '@/services/menuService';
import { Product } from '@/services/serverService';
import AllergenDisplay from '@/components/common/AllergenDisplay';
import styles from './ProductCustomization.module.css';

export interface DetailedIngredient {
  id: string;
  name: string;
  isActive: boolean;
  isOptional: boolean;
  price?: number;
  content?: {
    en?: { name: string };
    de?: { name: string };
    tr?: { name: string };
  };
}

export interface ProductVariation {
  id: string;
  name: string;
  description?: string;
  priceModifier: number;
  finalPrice: number;
  isActive: boolean;
  displayOrder: number;
  content?: {
    en?: { name: string; description?: string };
    de?: { name: string; description?: string };
    tr?: { name: string; description?: string };
  };
}

export interface SuggestedSideItem {
  id: string;
  name: string;
  description?: string;
  price: number;
  isRequired: boolean;
  displayOrder: number;
}

export interface CustomizationResult {
  productId: string;
  variationId?: string;
  variationName?: string;
  excludedIngredients: string[];
  addedIngredients: Array<{ id: string; name: string; price: number }>;
  sideItems: Array<{ id: string; name: string; quantity: number; price: number }>;
  specialInstructions?: string;
  finalPrice: number;
}

interface ProductCustomizationProps {
  product: Product;
  isOpen: boolean;
  onClose: () => void;
  onConfirm: (result: CustomizationResult) => void;
}

export default function ProductCustomization({
  product,
  isOpen,
  onClose,
  onConfirm,
}: ProductCustomizationProps) {
  const { t, i18n } = useTranslation();
  const currentLanguage = i18n.language.split('-')[0] || 'en';

  const [isLoading, setIsLoading] = useState(true);
  const [detailedProduct, setDetailedProduct] = useState<any>(null);
  const [selectedVariation, setSelectedVariation] = useState<ProductVariation | null>(null);
  const [excludedIngredients, setExcludedIngredients] = useState<Set<string>>(new Set());
  const [addedOptionalIngredients, setAddedOptionalIngredients] = useState<Set<string>>(new Set());
  const [selectedSideItems, setSelectedSideItems] = useState<Map<string, number>>(new Map());
  const [specialInstructions, setSpecialInstructions] = useState('');
  const [quantity, setQuantity] = useState(1);

  // Fetch detailed product data when modal opens
  useEffect(() => {
    if (!isOpen || !product) return;

    async function fetchDetails() {
      setIsLoading(true);
      try {
        const response = await getProductById(product.id) as { success: boolean; data?: any };
        if (response.success && response.data) {
          setDetailedProduct(response.data);

          // Reset selections
          setExcludedIngredients(new Set());
          setAddedOptionalIngredients(new Set());
          setSelectedSideItems(new Map());
          setSelectedVariation(null);
          setSpecialInstructions('');
          setQuantity(1);

          // Initialize required side items
          if (response.data.suggestedSideItems) {
            const requiredSides = new Map<string, number>();
            response.data.suggestedSideItems
              .filter((s: SuggestedSideItem) => s.isRequired)
              .forEach((s: SuggestedSideItem) => requiredSides.set(s.id, 1));
            setSelectedSideItems(requiredSides);
          }
        }
      } catch (err) {
        console.error('Failed to fetch product details:', err);
      } finally {
        setIsLoading(false);
      }
    }

    fetchDetails();
  }, [isOpen, product?.id]);

  // Get name in current language
  const getLocalizedName = (item: any): string => {
    return item.content?.[currentLanguage]?.name || item.content?.en?.name || item.name;
  };

  // Calculate total price
  const totalPrice = useMemo(() => {
    let basePrice = selectedVariation?.finalPrice ?? product.basePrice;

    // Add optional ingredient prices
    if (detailedProduct?.detailedIngredients) {
      detailedProduct.detailedIngredients.forEach((ing: DetailedIngredient) => {
        if (ing.isOptional && addedOptionalIngredients.has(ing.id) && ing.price) {
          basePrice += ing.price;
        }
      });
    }

    // Add side item prices
    if (detailedProduct?.suggestedSideItems) {
      detailedProduct.suggestedSideItems.forEach((side: SuggestedSideItem) => {
        const qty = selectedSideItems.get(side.id) || 0;
        basePrice += side.price * qty;
      });
    }

    return basePrice * quantity;
  }, [product.basePrice, selectedVariation, addedOptionalIngredients, selectedSideItems, detailedProduct, quantity]);

  // Get standard (non-optional) ingredients
  const standardIngredients = useMemo(() => {
    if (!detailedProduct?.detailedIngredients) return [];
    return detailedProduct.detailedIngredients.filter(
      (ing: DetailedIngredient) => ing.isActive && !ing.isOptional
    );
  }, [detailedProduct]);

  // Get optional ingredients
  const optionalIngredients = useMemo(() => {
    if (!detailedProduct?.detailedIngredients) return [];
    return detailedProduct.detailedIngredients.filter(
      (ing: DetailedIngredient) => ing.isActive && ing.isOptional
    );
  }, [detailedProduct]);

  // Get variations
  const variations = useMemo(() => {
    if (!detailedProduct?.variations) return [];
    return detailedProduct.variations.filter((v: ProductVariation) => v.isActive);
  }, [detailedProduct]);

  // Get side items
  const sideItems = useMemo(() => {
    return detailedProduct?.suggestedSideItems || [];
  }, [detailedProduct]);

  // Check if product has customizations
  const hasCustomizations = standardIngredients.length > 0 ||
    optionalIngredients.length > 0 ||
    variations.length > 0 ||
    sideItems.length > 0;

  // Toggle excluded ingredient
  const toggleExcluded = (ingredientId: string) => {
    const newSet = new Set(excludedIngredients);
    if (newSet.has(ingredientId)) {
      newSet.delete(ingredientId);
    } else {
      newSet.add(ingredientId);
    }
    setExcludedIngredients(newSet);
  };

  // Toggle optional ingredient
  const toggleOptional = (ingredientId: string) => {
    const newSet = new Set(addedOptionalIngredients);
    if (newSet.has(ingredientId)) {
      newSet.delete(ingredientId);
    } else {
      newSet.add(ingredientId);
    }
    setAddedOptionalIngredients(newSet);
  };

  // Toggle side item
  const toggleSideItem = (sideItemId: string) => {
    const newMap = new Map(selectedSideItems);
    if (newMap.has(sideItemId)) {
      newMap.delete(sideItemId);
    } else {
      newMap.set(sideItemId, 1);
    }
    setSelectedSideItems(newMap);
  };

  // Handle confirm
  const handleConfirm = () => {
    const result: CustomizationResult = {
      productId: product.id,
      variationId: selectedVariation?.id,
      variationName: selectedVariation ? getLocalizedName(selectedVariation) : undefined,
      excludedIngredients: Array.from(excludedIngredients),
      addedIngredients: Array.from(addedOptionalIngredients).map(id => {
        const ing = optionalIngredients.find((i: DetailedIngredient) => i.id === id);
        return { id, name: getLocalizedName(ing), price: ing?.price || 0 };
      }),
      sideItems: Array.from(selectedSideItems.entries()).map(([id, qty]) => {
        const side = sideItems.find((s: SuggestedSideItem) => s.id === id);
        return { id, name: side?.name || '', quantity: qty, price: side?.price || 0 };
      }),
      specialInstructions: specialInstructions || undefined,
      finalPrice: totalPrice / quantity, // Price per unit
    };

    // Add to cart with quantity
    for (let i = 0; i < quantity; i++) {
      onConfirm(result);
    }
    onClose();
  };

  if (!isOpen) return null;

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div className={styles.header}>
          <div className={styles.productInfo}>
            <h2 className={styles.productName}>{product.name}</h2>
            <span className={styles.basePrice}>CHF {product.basePrice.toFixed(2)}</span>
          </div>
          <button className={styles.closeButton} onClick={onClose}>✕</button>
        </div>

        <div className={styles.content}>
          {isLoading ? (
            <div className={styles.loading}>
              <div className={styles.spinner}></div>
              <span>{t('server.loading_options', 'Loading options...')}</span>
            </div>
          ) : !hasCustomizations ? (
            <div className={styles.noCustomizations}>
              <p>{t('server.no_customizations', 'This product has no customization options')}</p>
            </div>
          ) : (
            <>
              {/* Variations */}
              {variations.length > 0 && (
                <div className={styles.section}>
                  <h3 className={styles.sectionTitle}>{t('server.select_variation', 'Select Size/Variation')}</h3>
                  <div className={styles.variationList}>
                    {variations.map((variation: ProductVariation) => (
                      <button
                        key={variation.id}
                        className={`${styles.variationButton} ${selectedVariation?.id === variation.id ? styles.selected : ''}`}
                        onClick={() => setSelectedVariation(selectedVariation?.id === variation.id ? null : variation)}
                      >
                        <span className={styles.variationName}>{getLocalizedName(variation)}</span>
                        <span className={styles.variationPrice}>
                          CHF {variation.finalPrice.toFixed(2)}
                          {variation.priceModifier !== 0 && (
                            <span className={styles.modifier}>
                              ({variation.priceModifier > 0 ? '+' : ''}{variation.priceModifier.toFixed(2)})
                            </span>
                          )}
                        </span>
                      </button>
                    ))}
                  </div>
                </div>
              )}

              {/* Allergen Information */}
              {detailedProduct?.allergens && detailedProduct.allergens.length > 0 && (
                <div className={styles.section}>
                  <h3 className={styles.sectionTitle}>{t('server.allergens', 'Allergens')}</h3>
                  <AllergenDisplay
                    allergens={detailedProduct.allergens}
                    id={`product-${product.id}`}
                    variant="admin"
                    showLabel={false}
                  />
                </div>
              )}

              {/* Standard Ingredients (read-only info) */}
              {standardIngredients.length > 0 && (
                <div className={styles.section}>
                  <h3 className={styles.sectionTitle}>{t('server.ingredients', 'Ingredients')}</h3>
                  <div className={styles.ingredientList}>
                    {standardIngredients.map((ing: DetailedIngredient) => (
                      <span
                        key={ing.id}
                        className={styles.ingredientTag}
                      >
                        {getLocalizedName(ing)}
                      </span>
                    ))}
                  </div>
                </div>
              )}

              {/* Optional Ingredients (extras) */}
              {optionalIngredients.length > 0 && (
                <div className={styles.section}>
                  <h3 className={styles.sectionTitle}>{t('server.extras', 'Extras')}</h3>
                  <div className={styles.ingredientList}>
                    {optionalIngredients.map((ing: DetailedIngredient) => (
                      <button
                        key={ing.id}
                        className={`${styles.ingredientButton} ${styles.optional} ${addedOptionalIngredients.has(ing.id) ? styles.added : ''}`}
                        onClick={() => toggleOptional(ing.id)}
                      >
                        {addedOptionalIngredients.has(ing.id) && <span className={styles.addIcon}>✓</span>}
                        {getLocalizedName(ing)}
                        {ing.price && <span className={styles.ingredientPrice}>+{ing.price.toFixed(2)}</span>}
                      </button>
                    ))}
                  </div>
                </div>
              )}

              {/* Side Items */}
              {sideItems.length > 0 && (
                <div className={styles.section}>
                  <h3 className={styles.sectionTitle}>{t('server.side_items', 'Side Items')}</h3>
                  <div className={styles.sideItemList}>
                    {sideItems.map((side: SuggestedSideItem) => (
                      <button
                        key={side.id}
                        className={`${styles.sideItemButton} ${selectedSideItems.has(side.id) ? styles.selected : ''}`}
                        onClick={() => !side.isRequired && toggleSideItem(side.id)}
                        disabled={side.isRequired}
                      >
                        <span className={styles.sideName}>
                          {side.name}
                          {side.isRequired && <span className={styles.requiredBadge}>*</span>}
                        </span>
                        <span className={styles.sidePrice}>+CHF {side.price.toFixed(2)}</span>
                      </button>
                    ))}
                  </div>
                </div>
              )}

              {/* Special Instructions */}
              <div className={styles.section}>
                <h3 className={styles.sectionTitle}>{t('server.special_instructions', 'Special Instructions')}</h3>
                <textarea
                  className={styles.instructionsInput}
                  placeholder={t('server.instructions_placeholder', 'e.g., Extra spicy, no onions...')}
                  value={specialInstructions}
                  onChange={(e) => setSpecialInstructions(e.target.value)}
                  rows={2}
                />
              </div>
            </>
          )}
        </div>

        <div className={styles.footer}>
          <div className={styles.quantityControl}>
            <button
              className={styles.qtyButton}
              onClick={() => setQuantity(Math.max(1, quantity - 1))}
            >
              −
            </button>
            <span className={styles.qtyValue}>{quantity}</span>
            <button
              className={styles.qtyButton}
              onClick={() => setQuantity(quantity + 1)}
            >
              +
            </button>
          </div>
          <button className={styles.confirmButton} onClick={handleConfirm}>
            {t('server.add_to_order', 'Add to Order')} · CHF {totalPrice.toFixed(2)}
          </button>
        </div>
      </div>
    </div>
  );
}
