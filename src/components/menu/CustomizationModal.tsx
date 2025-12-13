"use client";

import React, { useState, useEffect, useCallback } from "react";
import { createPortal } from "react-dom";
import { useTranslation } from "react-i18next";
import Image from "next/image";
import { X } from "lucide-react";
import type { MenuItem, DetailedProduct, ProductCustomization } from "@/types/menu";
import OptionalIngredientsSection from "./customization/OptionalIngredientsSection";
import SuggestedSideItemsSection from "./customization/SuggestedSideItemsSection";
import SpecialRequestSection from "./customization/SpecialRequestSection";
import VariationsSection from "./customization/VariationsSection";
import PriceCalculator from "./customization/PriceCalculator";
import styles from "./CustomizationModal.module.css";

interface CustomizationModalProps {
  isOpen: boolean;
  onClose: () => void;
  product: MenuItem | DetailedProduct;
  onAddToCart: (customization: ProductCustomization) => Promise<void>;
}

export default function CustomizationModal({
  isOpen,
  onClose,
  product,
  onAddToCart,
}: CustomizationModalProps) {
  const { t, i18n } = useTranslation();
  const currentLanguage = i18n.language.split("-")[0] || "en";

  // State for customizations
  const [quantity, setQuantity] = useState(1);
  const [selectedVariationId, setSelectedVariationId] = useState<string | null>(null);
  const [selectedIngredients, setSelectedIngredients] = useState<string[]>([]);
  const [ingredientQuantities, setIngredientQuantities] = useState<Record<string, number>>({});
  const [excludedIngredients, setExcludedIngredients] = useState<string[]>([]);
  const [selectedSideItems, setSelectedSideItems] = useState<
    Array<{ id: string; quantity: number }>
  >([]);
  const [specialInstructions, setSpecialInstructions] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);

  // Calculate total price
  const totalPrice = useCallback(() => {
    // Get base price depending on product type
    const basePrice =
      "basePrice" in product
        ? product.basePrice
        : (product as MenuItem).price || 0;

    let total = basePrice;

    // Apply variation price modifier (always additive)
    if (selectedVariationId && product.variations) {
      const variation = product.variations.find(
        (v) => ((v as any).id || v.name) === selectedVariationId
      );
      if (variation) {
        // priceModifier is always additive: basePrice + modifier
        total = basePrice + variation.priceModifier;
      }
    }

    // Add optional ingredients price
    if (product.detailedIngredients) {
      selectedIngredients.forEach((ingredientId) => {
        const ingredient = product.detailedIngredients?.find((i) => i.id === ingredientId);
        if (ingredient) {
          const qty = ingredientQuantities[ingredientId] || 1;
          total += ingredient.price * qty;
        }
      });
    }

    // Add side items price
    selectedSideItems.forEach((selectedItem) => {
      if ("suggestedSideItems" in product && Array.isArray(product.suggestedSideItems)) {
        const sideItem = product.suggestedSideItems.find((s: any) =>
          typeof s === 'object' && s.id === selectedItem.id
        );
        if (sideItem && typeof sideItem === 'object' && "price" in sideItem) {
          total += sideItem.price * selectedItem.quantity;
        }
      }
    });

    return total * quantity;
  }, [product, selectedVariationId, selectedIngredients, selectedSideItems, quantity]);

  // Reset state when modal opens/closes
  useEffect(() => {
    if (!isOpen) {
      setQuantity(1);
      setSelectedVariationId(null);
      setSelectedIngredients([]);
      setIngredientQuantities({});
      setExcludedIngredients([]);
      setSelectedSideItems([]);
      setSpecialInstructions("");
      setIsSubmitting(false);
    }
  }, [isOpen]);

  // Initialize default selected ingredients
  // 1. Non-optional ingredients should be selected by default
  // 2. Optional ingredients with isIncludedInBasePrice should be pre-selected
  useEffect(() => {
    if (isOpen && product.detailedIngredients) {
      const defaultSelected = product.detailedIngredients
        .filter((ing) => ing.isActive && (!ing.isOptional || ing.isIncludedInBasePrice))
        .map((ing) => ing.id);
      setSelectedIngredients(defaultSelected);

      // Initialize quantities for default selected ingredients
      const initialQuantities: Record<string, number> = {};
      defaultSelected.forEach(id => {
        initialQuantities[id] = 1;
      });
      setIngredientQuantities(initialQuantities);
    }
  }, [isOpen, product.detailedIngredients]);

  // Handle keyboard events
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === "Escape" && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener("keydown", handleEscape);
      document.body.style.overflow = "hidden";
    }

    return () => {
      document.removeEventListener("keydown", handleEscape);
      document.body.style.overflow = "unset";
    };
  }, [isOpen, onClose]);

  const handleAddToCart = async () => {
    setIsSubmitting(true);
    try {
      const customization: ProductCustomization = {
        productId: product.id,
        quantity,
        selectedVariationId,
        selectedIngredients,
        excludedIngredients,
        addedIngredients: selectedIngredients.filter((id) => {
          const ingredient = product.detailedIngredients?.find((i) => i.id === id);
          return ingredient?.isOptional;
        }),
        ingredientQuantities,
        selectedSideItems,
        specialInstructions: specialInstructions.trim() || undefined,
        totalPrice: totalPrice(),
      };

      await onAddToCart(customization);
      onClose();
    } catch {
      // Error is already handled by parent component
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleIngredientQuantityChange = (ingredientId: string, newQuantity: number) => {
    setIngredientQuantities(prev => ({
      ...prev,
      [ingredientId]: newQuantity
    }));
  };

  const handleQuantityChange = (delta: number) => {
    const newQuantity = quantity + delta;
    if (newQuantity >= 1 && newQuantity <= 99) {
      setQuantity(newQuantity);
    }
  };

  if (!isOpen) return null;

  // Get product name in current language
  const productName =
    ("content" in product &&
      (product.content?.[currentLanguage]?.name || product.content?.en?.name)) ||
    product.name;

  // Get product image
  const productImage =
    ("image" in product ? product.image : product.imageUrl) || "/images/placeholder-falafel.jpeg";

  // Get base price depending on product type
  const basePrice =
    "basePrice" in product
      ? product.basePrice
      : (product as MenuItem).price || 0;

  // Check if product has any customization options
  const hasIngredients = product.detailedIngredients && product.detailedIngredients.length > 0;
  const hasVariations = product.variations && product.variations.length > 0;
  const hasSuggestedSides =
    "suggestedSideItems" in product &&
    Array.isArray(product.suggestedSideItems) &&
    product.suggestedSideItems.length > 0;

  return createPortal(
    <div className={styles.modalOverlay} onClick={onClose} role="dialog" aria-modal="true">
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        {/* Header */}
        <div className={styles.modalHeader}>
          <div className={styles.productInfo}>
            <div className={styles.productImageWrapper}>
              <Image
                src={productImage}
                alt={productName}
                className={styles.productImage}
                width={80}
                height={80}
                style={{ objectFit: 'cover' }}
              />
            </div>
            <div className={styles.productDetails}>
              <h2 className={styles.productName}>{productName}</h2>
              <p className={styles.basePrice}>
                {t("base_price")}: CHF {basePrice.toFixed(2)}
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className={styles.closeButton}
            aria-label={t("close")}
            type="button"
          >
            <X size={24} />
          </button>
        </div>

        {/* Body */}
        <div className={styles.modalBody}>
          {/* Variations Section */}
          {hasVariations && (
            <VariationsSection
              variations={product.variations || []}
              selectedVariationId={selectedVariationId}
              onVariationChange={setSelectedVariationId}
              basePrice={basePrice}
              currentLanguage={currentLanguage}
            />
          )}

          {/* Ingredients Section - Show for ALL ingredients, not just optional */}
          {hasIngredients && (
            <OptionalIngredientsSection
              ingredients={product.detailedIngredients || []}
              selectedIngredients={selectedIngredients}
              ingredientQuantities={ingredientQuantities}
              onSelectionChange={setSelectedIngredients}
              onQuantityChange={handleIngredientQuantityChange}
              currentLanguage={currentLanguage}
            />
          )}

          {/* Suggested Side Items Section */}
          {hasSuggestedSides && (
            <SuggestedSideItemsSection
              sideItems={
                "suggestedSideItems" in product
                  ? (product.suggestedSideItems as any[]) || []
                  : []
              }
              selectedSideItems={selectedSideItems}
              onSelectionChange={setSelectedSideItems}
              currentLanguage={currentLanguage}
            />
          )}

          {/* Special Request Section */}
          <SpecialRequestSection
            specialInstructions={specialInstructions}
            onInstructionsChange={setSpecialInstructions}
          />
        </div>

        {/* Footer */}
        <div className={styles.modalFooter}>
          <PriceCalculator
            basePrice={basePrice}
            ingredients={product.detailedIngredients || []}
            selectedIngredients={selectedIngredients}
            ingredientQuantities={ingredientQuantities}
            sideItems={
              "suggestedSideItems" in product
                ? (product.suggestedSideItems as any[]) || []
                : []
            }
            selectedSideItems={selectedSideItems}
            quantity={quantity}
            onQuantityChange={handleQuantityChange}
            selectedVariationId={selectedVariationId}
            variations={product.variations || []}
          />
          <button
            onClick={handleAddToCart}
            className={styles.addToCartButton}
            disabled={isSubmitting}
            type="button"
          >
            {isSubmitting
              ? t("adding_to_cart", "Adding...")
              : t("add_to_order", "Add to Order")}
          </button>
        </div>
      </div>
    </div>,
    document.body
  );
}
