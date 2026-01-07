import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useCart } from '@/components/cart/CartContext';
import { useSnackbar } from 'notistack';
import type { FeaturedSpecial, FeaturedSpecialResponse, ProductCustomization } from '@/types/menu';
import { getFeaturedSpecial } from '@/services/menuService';

export function useFeaturedSpecial() {
  const { t } = useTranslation();
  const { addItem } = useCart();
  const { enqueueSnackbar } = useSnackbar();

  const [featuredSpecial, setFeaturedSpecial] = useState<FeaturedSpecial | null>(null);
  const [showFeaturedDetails, setShowFeaturedDetails] = useState(false);
  const [showFeaturedCustomization, setShowFeaturedCustomization] = useState(false);

  // Load featured special on mount
  useEffect(() => {
    const loadFeaturedSpecial = async () => {
      try {
        const response = await getFeaturedSpecial() as FeaturedSpecialResponse;
        if (response.success && response.data) {
          setFeaturedSpecial(response.data);
        }
      } catch {
        // Silently fail if featured special cannot be loaded
      }
    };

    loadFeaturedSpecial();
  }, []);

  const handleAddFeaturedToCart = useCallback(() => {
    if (!featuredSpecial) return;

    // Check if product has customization options
    const hasCustomizationOptions =
      (featuredSpecial.variations && featuredSpecial.variations.length > 0) ||
      (featuredSpecial.detailedIngredients && featuredSpecial.detailedIngredients.some(ing => ing.isOptional)) ||
      (featuredSpecial.suggestedSideItems && featuredSpecial.suggestedSideItems.length > 0);

    // If product has customization options, show customization modal
    if (hasCustomizationOptions) {
      setShowFeaturedCustomization(true);
      return;
    }

    // Otherwise, add directly to cart
    try {
      addItem({
        productId: featuredSpecial.id,
        quantity: 1,
      });

      enqueueSnackbar(t('item_added_to_cart', 'Item added to cart'), {
        variant: 'success',
        autoHideDuration: 2000,
      });
    } catch {
      enqueueSnackbar(t('error_adding_to_cart', 'Error adding item to cart'), {
        variant: 'error',
        autoHideDuration: 3000,
      });
    }
  }, [featuredSpecial, addItem, enqueueSnackbar, t]);

  const handleFeaturedCustomizationConfirm = useCallback(async (customization: ProductCustomization) => {
    if (!featuredSpecial) return;

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

      setShowFeaturedCustomization(false);
      enqueueSnackbar(t('item_added_to_cart', 'Item added to cart'), {
        variant: 'success',
        autoHideDuration: 2000,
      });
    } catch {
      enqueueSnackbar(t('error_adding_to_cart', 'Error adding item to cart'), {
        variant: 'error',
        autoHideDuration: 3000,
      });
    }
  }, [featuredSpecial, addItem, enqueueSnackbar, t]);

  const handleViewFeaturedDetails = useCallback(() => {
    setShowFeaturedDetails(true);
  }, []);

  const handleCloseFeaturedDetails = useCallback(() => {
    setShowFeaturedDetails(false);
  }, []);

  return {
    featuredSpecial,
    showFeaturedDetails,
    showFeaturedCustomization,
    handleAddFeaturedToCart,
    handleFeaturedCustomizationConfirm,
    handleViewFeaturedDetails,
    handleCloseFeaturedDetails,
    setShowFeaturedCustomization,
  };
}
