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
  const [selectedIngredients, setSelectedIngredients] = useState<string[]>([]);
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

    // Add optional ingredients price
    if (product.detailedIngredients) {
      selectedIngredients.forEach((ingredientId) => {
        const ingredient = product.detailedIngredients?.find((i) => i.id === ingredientId);
        if (ingredient) {
          total += ingredient.price;
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
  }, [product, selectedIngredients, selectedSideItems, quantity]);

  // Reset state when modal opens/closes
  useEffect(() => {
    if (!isOpen) {
      setQuantity(1);
      setSelectedIngredients([]);
      setExcludedIngredients([]);
      setSelectedSideItems([]);
      setSpecialInstructions("");
      setIsSubmitting(false);
    }
  }, [isOpen]);

  // Initialize default selected ingredients (non-optional ingredients should be selected by default)
  useEffect(() => {
    if (isOpen && product.detailedIngredients) {
      const defaultSelected = product.detailedIngredients
        .filter((ing) => !ing.isOptional && ing.isActive)
        .map((ing) => ing.id);
      setSelectedIngredients(defaultSelected);
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
        selectedIngredients,
        excludedIngredients,
        addedIngredients: selectedIngredients.filter((id) => {
          const ingredient = product.detailedIngredients?.find((i) => i.id === id);
          return ingredient?.isOptional;
        }),
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
  const hasOptionalIngredients = product.detailedIngredients?.some((i) => i.isOptional);
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
          {/* Optional Ingredients Section */}
          {hasOptionalIngredients && (
            <OptionalIngredientsSection
              ingredients={product.detailedIngredients || []}
              selectedIngredients={selectedIngredients}
              onSelectionChange={setSelectedIngredients}
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
            sideItems={
              "suggestedSideItems" in product
                ? (product.suggestedSideItems as any[]) || []
                : []
            }
            selectedSideItems={selectedSideItems}
            quantity={quantity}
            onQuantityChange={handleQuantityChange}
          />

          <button
            onClick={handleAddToCart}
            className={styles.addToCartButton}
            disabled={isSubmitting}
            type="button"
          >
            {isSubmitting
              ? t("adding_to_cart", "Adding...")
              : `${t("add_to_order")} ${quantity} ${t("to_cart")} - CHF ${totalPrice().toFixed(2)}`}
          </button>
        </div>
      </div>
    </div>,
    document.body
  );
}
