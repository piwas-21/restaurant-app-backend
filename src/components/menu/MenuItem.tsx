
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
  onFeedbackSuccess: (dishId: string) => void;
  getFallbackImage: (item: MenuItemType) => void;
}

const MenuItem: React.FC<MenuItemProps> = ({
  item,
  onFeedbackSuccess,
  getFallbackImage,
}) => {
  const { addItem } = useCart();
  const { t, i18n } = useTranslation();
  const { enqueueSnackbar } = useSnackbar();
  const [showFeedbackForm, setShowFeedbackForm] = useState<string | null>(null);
  const [showDetails, setShowDetails] = useState(false);
  const [showCustomization, setShowCustomization] = useState(false);
  const [detailedProduct, setDetailedProduct] = useState<MenuItemType | null>(null);
  const [isLoadingDetails, setIsLoadingDetails] = useState(false);

  // Get current language - component will re-render when i18n.language changes
  const currentLanguage = (i18n.language.split("-")[0] || "en") as LanguageCode;

  const handleAddItemToCart = useCallback(async () => {
    // Always fetch full product details first to check for customization options
    setIsLoadingDetails(true);
    try {
      const response = await fetch(`${process.env.NEXT_PUBLIC_API_URL}/api/products/${item.id}`);
      if (!response.ok) {
        throw new Error('Failed to fetch product details');
      }

      const result = await response.json();
      if (!result.success || !result.data) {
        throw new Error('Failed to load product details');
      }

      const fullProduct = result.data;

      // Check if product has customization options
      const hasCustomizationOptions =
        (fullProduct.variations && fullProduct.variations.length > 0) ||
        (fullProduct.detailedIngredients && fullProduct.detailedIngredients.length > 0) ||
        (fullProduct.suggestedSideItems && fullProduct.suggestedSideItems.length > 0);

      if (hasCustomizationOptions) {
        // Show customization modal
        setDetailedProduct(fullProduct);
        setShowCustomization(true);
        setIsLoadingDetails(false);
        return;
      }

      // No customization needed, add directly to cart
      setIsLoadingDetails(false);
      const itemName =
        fullProduct.content?.[currentLanguage]?.name || fullProduct.content?.en?.name || fullProduct.name;

      await addItem({
        productId: fullProduct.id,
        quantity: 1,
      });
      enqueueSnackbar(t("item_added_to_cart_toast", { itemName }), {
        variant: "success",
        autoHideDuration: 2000,
        anchorOrigin: { vertical: "top", horizontal: "center" },
      });
    } catch (error) {
      console.error('Error fetching product details:', error);
      setIsLoadingDetails(false);
      enqueueSnackbar(t("error_loading_product", "Failed to load product details"), {
        variant: "error",
      });
    }
  }, [addItem, enqueueSnackbar, t, currentLanguage, item.id]);

  const handleCustomizationConfirm = useCallback(async (customization: ProductCustomization) => {
    const itemName =
      item.content?.[currentLanguage]?.name || item.content?.en?.name || item.name;

    try {
      await addItem({
        productId: customization.productId,
        productVariationId: customization.selectedVariationId || undefined,
        quantity: customization.quantity,
        specialInstructions: customization.specialInstructions,
        selectedIngredients: customization.selectedIngredients,
        excludedIngredients: customization.excludedIngredients,
        ingredientQuantities: customization.ingredientQuantities,
        selectedSideItems: customization.selectedSideItems,
      });

      setShowCustomization(false);
      enqueueSnackbar(t("item_added_to_cart_toast", { itemName }), {
        variant: "success",
        autoHideDuration: 2000,
        anchorOrigin: { vertical: "top", horizontal: "center" },
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
        onClick={() => setShowDetails(true)}
        onError={() => getFallbackImage(item)}
      />
      <div className={styles.contentWrapper}>
        <MenuItemDetails
          id={item.id}
          title={itemName}
          description={productDescription}
          ingredients={ingredientsText}
          allergens={item.allergens}
          price={numericPrice}
          dietaryTags={item.dietaryTags}
          t={t}
          onTitleClick={() => setShowDetails(true)}
          initialRatingData={{ average: 0, count: 0 }}
        />
        <div className={styles.priceActionsRow}>
          <span className={styles.mobilePrice}>CHF {numericPrice.toFixed(2)}</span>
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
        </div>
      </div>
      {showFeedbackForm === item.id && (
        <FeedbackForm
          dishId={item.id}
          onSubmitSuccess={() => onFeedbackSuccess(item.id)}
        />
      )}
      <ProductDetailsModal isOpen={showDetails} item={item} onClose={() => setShowDetails(false)} />
      {showCustomization && detailedProduct && (
        <CustomizationModal
          product={detailedProduct}
          isOpen={showCustomization}
          onClose={() => {
            setShowCustomization(false);
            setDetailedProduct(null);
          }}
          onAddToCart={handleCustomizationConfirm}
        />
      )}
      {isLoadingDetails && (
        <div style={{
          position: 'fixed',
          top: '50%',
          left: '50%',
          transform: 'translate(-50%, -50%)',
          zIndex: 9999,
          background: 'rgba(0,0,0,0.8)',
          color: 'white',
          padding: '20px',
          borderRadius: '8px'
        }}>
          {t('loading', 'Loading...')}
        </div>
      )}

    </div>
  );
};

export default MenuItem;
