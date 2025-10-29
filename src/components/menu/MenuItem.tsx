
"use client";

import React, { useState, useCallback } from "react";
import { useCart } from "@/components/cart/CartContext";
import { useTranslation } from "react-i18next";
import { useSnackbar } from "notistack";
import type { LanguageCode } from "@/components/LanguageSwitcher";
import type { MenuItem as MenuItemType, ProductCustomization } from "@/types/menu";
import MenuItemImage from "./MenuItemImage";
import MenuItemDetails from "./MenuItemDetails";
import MenuItemActions from "./MenuItemActions";
import ProductDetailsModal from "./ProductDetailsModal";
import CustomizationModal from "./CustomizationModal";
import FeedbackForm from "@/components/feedback/FeedbackForm";
import styles from "./MenuItem.module.css";

interface MenuItemProps {
  item: MenuItemType;
  onImageClick: (item: MenuItemType, imageIndex?: number) => void;
  onFeedbackSuccess: (dishId: string) => void;
  getFallbackImage: (item: MenuItemType) => void;
}

const MenuItem: React.FC<MenuItemProps> = ({
  item,
  onImageClick,
  onFeedbackSuccess,
  getFallbackImage,
}) => {
  const { addItem } = useCart();
  const { t, i18n } = useTranslation();
  const { enqueueSnackbar } = useSnackbar();
  const [showFeedbackForm, setShowFeedbackForm] = useState<string | null>(null);
  const [showDetails, setShowDetails] = useState(false);
  const [showCustomization, setShowCustomization] = useState(false);

  // Get current language - component will re-render when i18n.language changes
  const currentLanguage = (i18n.language.split("-")[0] || "en") as LanguageCode;

  // Check if product has customization options
  const hasCustomizationOptions =
    (item.detailedIngredients && item.detailedIngredients.some(ing => ing.isOptional)) ||
    (item.suggestedSideItems && item.suggestedSideItems.length > 0);

  const handleAddItemToCart = useCallback(async () => {
    // If product has customization options, show customization modal
    if (hasCustomizationOptions) {
      setShowCustomization(true);
      return;
    }

    // Otherwise, add directly to cart
    const itemName =
      item.content?.[currentLanguage]?.name || item.content?.en?.name || item.name;

    try {
      await addItem({
        productId: item.id,
        quantity: 1,
      });
      enqueueSnackbar(t("item_added_to_cart_toast", { itemName }), {
        variant: "success",
      });
    } catch {
      enqueueSnackbar(t("error_adding_to_cart", "Failed to add item to cart"), {
        variant: "error",
      });
    }
  }, [addItem, enqueueSnackbar, t, currentLanguage, item, hasCustomizationOptions]);

  const handleCustomizationConfirm = useCallback(async (customization: ProductCustomization) => {
    const itemName =
      item.content?.[currentLanguage]?.name || item.content?.en?.name || item.name;

    try {
      await addItem({
        productId: customization.productId,
        quantity: customization.quantity,
        specialInstructions: customization.specialInstructions,
        selectedIngredients: customization.selectedIngredients,
        excludedIngredients: customization.excludedIngredients,
        selectedSideItems: customization.selectedSideItems,
      });

      setShowCustomization(false);
      enqueueSnackbar(t("item_added_to_cart_toast", { itemName }), {
        variant: "success",
      });
    } catch {
      enqueueSnackbar(t("error_adding_to_cart", "Failed to add item to cart"), {
        variant: "error",
      });
    }
  }, [addItem, enqueueSnackbar, t, currentLanguage, item]);

  // Compute localized values - these will update when currentLanguage changes
  const itemName =
    item.content?.[currentLanguage]?.name || item.content?.en?.name || item.name;

  // Get ingredients from detailedIngredients with multilingual support
  const getIngredientsText = () => {
    if (item.detailedIngredients && item.detailedIngredients.length > 0) {
      return item.detailedIngredients
        .filter((ing: any) => ing.isActive)
        .map((ing: any) => {
          // Try to get name in current language, fallback to English, then base name
          return ing.content?.[currentLanguage]?.name || ing.content?.en?.name || ing.name;
        })
        .join(', ');
    }
    // Fallback to legacy ingredients array
    return Array.isArray(item.ingredients) ? item.ingredients.join(', ') : '';
  };

  const ingredientsText = getIngredientsText();

  const productDescription =
    item.content?.[currentLanguage]?.description ||
    item.content?.en?.description ||
    item.longDescription ||
    "";

  const mainImageAlt =
    item.content?.[currentLanguage]?.name ||
    item.content?.en?.name ||
    item.name ||
    t("menu_item_image_alt");
  const numericPrice =
    typeof item.price === "string" ? parseFloat(item.price) : item.price;

  return (
    <div
      className={styles.menuItem}
      role="listitem"
      aria-labelledby={`item-name-${item.id}`}
    >
      <MenuItemImage
        imageUrl={item.image}
        alt={mainImageAlt}
        imageCount={item.images?.length}
        countLabel={t("images_count_label")}
        onClick={() => onImageClick(item, 0)}
        onError={() => getFallbackImage(item)}
      />
      <MenuItemDetails
        id={item.id}
        title={itemName}
        description={productDescription}
        ingredients={ingredientsText}
        allergens={item.allergens}
        price={numericPrice}
        dietaryTags={item.dietaryTags}
        t={t}
        initialRatingData={{ average: 0, count: 0 }}
      />
      <MenuItemActions
        onAdd={handleAddItemToCart}
        onFeedback={() => setShowFeedbackForm(item.id)}
        addAria={t("add_item_to_order", { itemName })}
        addLabel={t("add_to_order")}
        onDetails={() => setShowDetails(true)}
        detailsLabel={t('details')}
        feedbackAria={`${t("feedback_form_heading")} ${itemName}`}
        feedbackLabel={t("feedback_form_heading")}
      />
      {showFeedbackForm === item.id && (
        <FeedbackForm
          dishId={item.id}
          onSubmitSuccess={() => onFeedbackSuccess(item.id)}
        />
      )}
      <ProductDetailsModal isOpen={showDetails} item={item} onClose={() => setShowDetails(false)} />
      {showCustomization && (
        <CustomizationModal
          product={item}
          isOpen={showCustomization}
          onClose={() => setShowCustomization(false)}
          onAddToCart={handleCustomizationConfirm}
        />
      )}
    </div>
  );
};

export default MenuItem;
