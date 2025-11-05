"use client";

import React, { useState, useEffect, useCallback } from "react";
import styles from "../styles/MenuPage.module.css";
import Link from "next/link";
import { useTranslation } from "react-i18next";
import { UtensilsCrossed } from "lucide-react";
import { useCart } from "@/components/cart/CartContext";
import { useSnackbar } from "notistack";
import TableBanner from "@/components/TableBanner";

import type { LanguageCode } from "@/components/LanguageSwitcher";
import { usePublicMenu, ALL_ITEMS_KEY } from "@/hooks/usePublicMenu";
import CategoryNav from "@/components/menu/CategoryNav";
import type { MenuItem, FeaturedSpecial, FeaturedSpecialResponse, ProductCustomization } from "@/types/menu";
import ImageModal from "@/components/menu/ImageModal";
import MenuList from "@/components/menu/MenuList";
import Pagination from "@/components/common/Pagination";
import FeaturedSpecialComponent from "@/components/menu/FeaturedSpecial";
import ProductDetailsModal from "@/components/menu/ProductDetailsModal";
import CustomizationModal from "@/components/menu/CustomizationModal";
import { getFeaturedSpecial } from "@/services/menuService";

// Map API category names to translation keys
function mapCategoryNameToTranslationKey(apiCategoryName: string): string {
  const mapping: { [key: string]: string } = {
    'Starters': 'starters',
    'Grills': 'grill',
    'Grill': 'grill',
    'Dessert': 'dessert',
    'Desserts': 'dessert',
    'Dürüm Wraps': 'durum',
    'Durum Wraps': 'durum',
    'Hot Drinks': 'hotDrink',
    'Cold Drinks': 'coldDrink',
    'Drinks': 'hotDrink', // Default to hot drinks, might need more logic
    'Pizza': 'pizza',
    'Pide': 'pide',
    'Turkish Specialties': 'turkishSpecialty',
    'Oriental Specialties': 'orientalSpecialty',
    'Special of the Day': 'specialOfTheDay',
    'Soups': 'soups'
  };

  return mapping[apiCategoryName] || apiCategoryName.toLowerCase();
}

export default function MenuPage() {
  const { t, i18n } = useTranslation();

  const {
    categories: categoriesForNav,
    selectedView,
    setSelectedView,
    items: currentMenuItems,
    isLoading: isLoadingItems,
    error: errorLoadingItems,
    currentPage,
    totalPages,
    totalCount,
    onPageChange,
  } = usePublicMenu();

  const [enlargedImageItem, setEnlargedImageItem] = useState<MenuItem | null>(
    null
  );
  const [currentImageIndex, setCurrentImageIndex] = useState(0);
  const [isMounted, setIsMounted] = useState(false);
  const [featuredSpecial, setFeaturedSpecial] = useState<FeaturedSpecial | null>(null);
  const [showFeaturedDetails, setShowFeaturedDetails] = useState(false);
  const [showFeaturedCustomization, setShowFeaturedCustomization] = useState(false);

  const { addItem } = useCart();
  const { enqueueSnackbar } = useSnackbar();

  const currentLanguage = (i18n.language.split("-")[0] || "en") as LanguageCode;

  const getFallbackImage = (menuItem: MenuItem) => {
    const fallbackImage = "/images/placeholder-falafel.jpeg";
    if (menuItem && menuItem.image !== fallbackImage) {
      menuItem.image = fallbackImage;
    }
  };

  useEffect(() => {
    setIsMounted(true);

    // Fetch featured special
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

  useEffect(() => {
    if (!isMounted || !selectedView) return;
    // error comes localized below for display only, so nothing here
  }, [isMounted, selectedView]);

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

  const handleImageClick = useCallback(
    (item: MenuItem, imageIndex: number = 0) => {
      setEnlargedImageItem(item);
      const initialImageIndex =
        item.images && item.images.length > imageIndex ? imageIndex : 0;
      setCurrentImageIndex(initialImageIndex);
    },
    []
  );

  const handleCloseEnlargedImage = useCallback(() => {
    setEnlargedImageItem(null);
    setCurrentImageIndex(0);
  }, []);

  const getEnlargedImages = useCallback(() => {
    if (!enlargedImageItem) return [];
    if (enlargedImageItem.images && enlargedImageItem.images.length > 0) {
      return enlargedImageItem.images;
    }
    const altText =
      enlargedImageItem.content?.[currentLanguage]?.name ||
      enlargedImageItem.content?.en?.name ||
      enlargedImageItem.name ||
      'Menu item image';
    return [{ url: enlargedImageItem.image, alt: altText }];
  }, [enlargedImageItem, currentLanguage]);

  const currentEnlargedGalleryImages = getEnlargedImages();

  const showNextImage = useCallback(() => {
    setCurrentImageIndex(
      (prevIndex) => (prevIndex + 1) % currentEnlargedGalleryImages.length
    );
  }, [currentEnlargedGalleryImages]);

  const showPrevImage = useCallback(() => {
    setCurrentImageIndex(
      (prevIndex) =>
        (prevIndex - 1 + currentEnlargedGalleryImages.length) %
        currentEnlargedGalleryImages.length
    );
  }, [currentEnlargedGalleryImages]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (!enlargedImageItem) return;
      if (event.key === "ArrowRight" && currentEnlargedGalleryImages.length > 1)
        showNextImage();
      if (event.key === "ArrowLeft" && currentEnlargedGalleryImages.length > 1)
        showPrevImage();
      if (event.key === "Escape") handleCloseEnlargedImage();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [
    enlargedImageItem,
    showNextImage,
    showPrevImage,
    handleCloseEnlargedImage,
    currentEnlargedGalleryImages,
  ]);

  if (!isMounted || !selectedView) {
    return null;
  }

  const categoryDisplayName =
    selectedView === ALL_ITEMS_KEY
      ? t("all_categories_nav")
      : (() => {
          const category = categoriesForNav.find((c) => c.id === selectedView);
          if (!category) return String(selectedView);

          const translationKey = mapCategoryNameToTranslationKey(category.name);
          const translatedName = t(translationKey);

          // If translation exists and is different from the key, use it; otherwise use API name
          return translatedName !== translationKey ? translatedName : category.name;
        })();
  const displayError = errorLoadingItems
    ? t(
        selectedView === ALL_ITEMS_KEY
          ? "error_loading_all_menu_items"
          : "error_loading_menu_items",
        {
          categoryName: categoryDisplayName,
        }
      )
    : null;

  return (
    <main className={styles.menuContainer} aria-labelledby="menu-page-heading">
      <h1 id="menu-page-heading" className={styles.pageTitle}>
        <UtensilsCrossed size={48} strokeWidth={2} aria-label={t("menu_title")} />
      </h1>

      <TableBanner position="top" />

      {featuredSpecial && (
        <FeaturedSpecialComponent
          special={featuredSpecial}
          onAddToCart={handleAddFeaturedToCart}
          onViewDetails={handleViewFeaturedDetails}
        />
      )}

      {categoriesForNav.length > 0 && (
        <CategoryNav
          categories={categoriesForNav}
          selectedView={selectedView}
          onSelect={setSelectedView}
          allLabel={t("all_categories_nav")}
        />
      )}

      <section
        className={styles.categorySection}
        aria-labelledby={`category-heading-${selectedView}`}
      >
        <h2
          id={`category-heading-${selectedView}`}
          className={styles.categoryTitle}
        >
          {categoryDisplayName}
        </h2>
        {isLoadingItems && <p>{t("loading_items", "Loading items...")}</p>}
        {displayError && <p className={styles.errorMessage}>{displayError}</p>}
        {!isLoadingItems && !displayError && currentMenuItems.length === 0 && (
          <p>
            {t("no_items_in_category", { categoryName: categoryDisplayName })}
          </p>
        )}
        {!isLoadingItems && !displayError && currentMenuItems.length > 0 && (
          <>
            <MenuList
              items={currentMenuItems}
              onImageClick={handleImageClick}
              onFeedbackSuccess={() => {}}
              getFallbackImage={getFallbackImage}
            />
            <Pagination
              currentPage={currentPage}
              totalPages={totalPages}
              onPageChange={onPageChange}
              isLoading={isLoadingItems}
            />
            {totalCount > 0 && (
              <p className={styles.paginationInfo}>
                {t('showing_items', {
                  start: (currentPage - 1) * 10 + 1,
                  end: Math.min(currentPage * 10, totalCount),
                  total: totalCount,
                  defaultValue: `Showing ${(currentPage - 1) * 10 + 1}-${Math.min(currentPage * 10, totalCount)} of ${totalCount} items`
                })}
              </p>
            )}
          </>
        )}
      </section>

      {enlargedImageItem && currentEnlargedGalleryImages.length > 0 && (
        <ImageModal
          isOpen={true}
          images={currentEnlargedGalleryImages}
          currentIndex={currentImageIndex}
          onClose={handleCloseEnlargedImage}
          onNext={showNextImage}
          onPrev={showPrevImage}
          altBase={
            enlargedImageItem.content?.[currentLanguage]?.name ||
            enlargedImageItem.content?.en?.name ||
            enlargedImageItem.id
          }
          onImageError={() => getFallbackImage(enlargedImageItem)}
          previousLabel={t("previous_image_button_label")}
          nextLabel={t("next_image_button_label")}
          closeLabel={t("close_image_modal_button", "Close image modal")}
        />
      )}

      {showFeaturedDetails && featuredSpecial && (
        <ProductDetailsModal
          isOpen={showFeaturedDetails}
          item={{
            id: featuredSpecial.id,
            name: featuredSpecial.name,
            description: featuredSpecial.description || '',
            price: featuredSpecial.basePrice,
            image: featuredSpecial.imageUrl || '',
            preparationTimeMinutes: featuredSpecial.preparationTimeMinutes,
            allergens: featuredSpecial.allergens,
            ingredients: featuredSpecial.ingredients,
            dietaryTags: [],
            content: {},
            images: featuredSpecial.imageUrl ? [{ url: featuredSpecial.imageUrl, alt: featuredSpecial.name }] : [],
            categoryKey: 'specials',
            isSpecial: true,
          }}
          onClose={handleCloseFeaturedDetails}
        />
      )}

      {showFeaturedCustomization && featuredSpecial && (
        <CustomizationModal
          isOpen={showFeaturedCustomization}
          product={{
            id: featuredSpecial.id,
            name: featuredSpecial.name,
            description: featuredSpecial.description || '',
            basePrice: featuredSpecial.basePrice,
            imageUrl: featuredSpecial.imageUrl || '',
            preparationTimeMinutes: featuredSpecial.preparationTimeMinutes,
            allergens: featuredSpecial.allergens || [],
            ingredients: featuredSpecial.ingredients || [],
            detailedIngredients: featuredSpecial.detailedIngredients || [],
            displayOrder: 0,
            type: 'mainItem',
            isActive: true,
            isAvailable: true,
            isSpecial: true,
            content: {},
            images: featuredSpecial.images || (featuredSpecial.imageUrl ? [{ url: featuredSpecial.imageUrl, alt: featuredSpecial.name }] : []),
            variations: featuredSpecial.variations || [],
            suggestedSideItems: featuredSpecial.suggestedSideItems || [],
            categories: [],
          }}
          onClose={() => setShowFeaturedCustomization(false)}
          onAddToCart={handleFeaturedCustomizationConfirm}
        />
      )}

      <div style={{ textAlign: "center", marginTop: "2rem" }}>
        <Link
          href="/cart"
          className={`${styles.viewCartButton}`}
        >
          {t("view_cart_checkout_button")}
        </Link>
      </div>
    </main>
  );
}
