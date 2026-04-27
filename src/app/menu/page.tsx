"use client";

import React, { useState, useEffect } from "react";
import styles from "../styles/MenuPage.module.css";
import Link from "next/link";
import { useTranslation } from "react-i18next";
import { useSnackbar } from "notistack";
import TableBanner from "@/components/TableBanner";

import type { LanguageCode } from "@/components/LanguageSwitcher";
import { usePublicMenu, ALL_ITEMS_KEY, MENU_BUNDLES_KEY } from "@/hooks/usePublicMenu";
import { useFeaturedSpecial } from "@/hooks/useFeaturedSpecial";
import { useCart } from "@/components/cart/CartContext";
import { getCategoryDisplayName } from "@/utils/categoryNameMapper";
import { setFallbackImage } from "@/utils/imageHelpers";

import MenuPageHeader from "@/components/menu/MenuPageHeader";
import MenuContent from "@/components/menu/MenuContent";
import MenuModals from "@/components/menu/MenuModals";
import FeaturedSpecialComponent from "@/components/menu/FeaturedSpecial";
import MenuBundleDetailsModal from "@/components/menu/MenuBundleDetailsModal";
import { MenuBundleItem, SelectedMenuOption } from '@/types/menu';
import MenuCustomizationModal from '@/components/menu/MenuCustomizationModal';
import FloatingCartButton from '@/components/menu/FloatingCartButton';

export default function MenuPage() {
  const { t, i18n } = useTranslation();
  const [isMounted, setIsMounted] = useState(false);
  const [selectedBundle, setSelectedBundle] = useState<MenuBundleItem | null>(null);
  const [showBundleDetails, setShowBundleDetails] = useState(false);
  const [selectedBundleForCustomization, setSelectedBundleForCustomization] = useState<MenuBundleItem | null>(null);
  const [cartAnimationTrigger, setCartAnimationTrigger] = useState(false);

  const currentLanguage = (i18n.language.split("-")[0] || "en") as LanguageCode;

  // Custom hooks
  const {
    categories: categoriesForNav,
    selectedView,
    setSelectedView,
    items: currentMenuItems,
    menuBundles,
    isLoading: isLoadingItems,
    error: errorLoadingItems,
    currentPage,
    totalPages,
    totalCount,
    onPageChange,
  } = usePublicMenu();

  const {
    featuredSpecial,
    showFeaturedDetails,
    showFeaturedCustomization,
    handleAddFeaturedToCart,
    handleFeaturedCustomizationConfirm,
    handleViewFeaturedDetails,
    handleCloseFeaturedDetails,
    setShowFeaturedCustomization,
  } = useFeaturedSpecial();

  const { addItem, state: cartState } = useCart();
  const { enqueueSnackbar } = useSnackbar();

  useEffect(() => {
    setIsMounted(true);
  }, []);

  // Bundle handlers
  const handleCustomizeBundle = (bundle: MenuBundleItem) => {
    setSelectedBundleForCustomization(bundle);
  };

  const handleAddBundleToCart = async (
    bundle: MenuBundleItem,
    selectedOptions?: SelectedMenuOption[],
    totalPrice?: number
  ) => {
    const bundleName =
      bundle.content?.[currentLanguage]?.name ||
      bundle.content?.en?.name ||
      bundle.name;

    try {
      if (!bundle.menuDefinition) {
        enqueueSnackbar(t('error_adding_to_cart', 'Failed to add bundle to cart'), {
          variant: 'error',
        });
        return;
      }

      // If selectedOptions are provided (from customization modal), use them
      // Otherwise use default items (respecting maxSelection)
      const optionsToAdd = selectedOptions || bundle.menuDefinition.sections?.flatMap((section) => {
        const defaultItems = section.items.filter((item) => item.isDefault);

        // Respect maxSelection when selecting defaults
        const itemsToSelect = defaultItems.slice(0, section.maxSelection);

        return itemsToSelect.map((item) => ({
          sectionId: section.id,
          itemId: item.productId,
          quantity: 1,
        }));
      }) || [];

      // Use provided totalPrice or bundle base price
      const price = totalPrice || bundle.basePrice;

      await addItem({
        productId: bundle.id,
        quantity: 1,
        selectedMenuOptions: optionsToAdd,
      });

      // Close customization modal if open
      setSelectedBundleForCustomization(null);

      enqueueSnackbar(t('item_added_to_cart_toast', { itemName: bundleName }), {
        variant: 'success',
        autoHideDuration: 2000,
        anchorOrigin: { vertical: 'top', horizontal: 'center' },
      });

      // Trigger cart animation
      setCartAnimationTrigger(true);
      setTimeout(() => setCartAnimationTrigger(false), 100);
    } catch (error) {
      console.error('Error adding bundle to cart:', error);
      enqueueSnackbar(t('error_adding_to_cart', 'Failed to add bundle to cart'), {
        variant: 'error',
      });
    }
  };

  const handleViewBundleDetails = (bundle: MenuBundleItem) => {
    setSelectedBundle(bundle);
    setShowBundleDetails(true);
  };

  if (!isMounted || !selectedView) {
    return null;
  }

  // Get display name for selected category
  const categoryDisplayName =
    selectedView === ALL_ITEMS_KEY
      ? t("all_categories_nav")
      : selectedView === MENU_BUNDLES_KEY
      ? t("menu_bundles")
      : (() => {
          const category = categoriesForNav.find((c) => c.id === selectedView);
          if (!category) return String(selectedView);
          return getCategoryDisplayName(category.name, t);
        })();

  // Calculate cart totals for floating button
  const itemCount = cartState.items.reduce((sum, item) => sum + item.quantity, 0);
  const cartTotal = cartState.basket?.total || 0;

  return (
    <main className={styles.menuContainer} aria-labelledby="menu-page-heading">
      <MenuPageHeader />

      <TableBanner position="top" />

      {featuredSpecial && (
        <FeaturedSpecialComponent
          special={featuredSpecial}
          onAddToCart={handleAddFeaturedToCart}
          onViewDetails={handleViewFeaturedDetails}
        />
      )}

      <MenuContent
        categoriesForNav={categoriesForNav}
        selectedView={selectedView}
        onSelectView={setSelectedView}
        categoryDisplayName={categoryDisplayName}
        isLoadingItems={isLoadingItems}
        errorLoadingItems={errorLoadingItems}
        currentMenuItems={currentMenuItems}
        menuBundles={menuBundles}
        currentPage={currentPage}
        totalPages={totalPages}
        totalCount={totalCount}
        onPageChange={onPageChange}
        getFallbackImage={setFallbackImage}
        currentLanguage={currentLanguage}
        onAddBundleToCart={handleCustomizeBundle}
        onViewBundleDetails={handleViewBundleDetails}
      />

      <MenuModals
        featuredSpecial={featuredSpecial}
        showFeaturedDetails={showFeaturedDetails}
        showFeaturedCustomization={showFeaturedCustomization}
        onCloseFeaturedDetails={handleCloseFeaturedDetails}
        onCloseFeaturedCustomization={() => setShowFeaturedCustomization(false)}
        onFeaturedCustomizationConfirm={handleFeaturedCustomizationConfirm}
      />

      {/* Menu Bundle Details Modal */}
      <MenuBundleDetailsModal
        bundle={selectedBundle}
        isOpen={showBundleDetails}
        onClose={() => setShowBundleDetails(false)}
        onAddToCart={handleCustomizeBundle}
        currentLanguage={currentLanguage}
      />

      {/* Menu Customization Modal */}
      {selectedBundleForCustomization && selectedBundleForCustomization.menuDefinition && (
        <MenuCustomizationModal
          isOpen={!!selectedBundleForCustomization}
          onClose={() => setSelectedBundleForCustomization(null)}
          productId={selectedBundleForCustomization.id}
          productName={
            selectedBundleForCustomization.content?.[currentLanguage]?.name ||
            selectedBundleForCustomization.content?.en?.name ||
            selectedBundleForCustomization.name
          }
          basePrice={selectedBundleForCustomization.basePrice}
          menuDefinition={selectedBundleForCustomization.menuDefinition}
          onAddToBasket={(selectedOptions, totalPrice) =>
            handleAddBundleToCart(selectedBundleForCustomization, selectedOptions, totalPrice)
          }
          currentLanguage={currentLanguage}
        />
      )}

      <div style={{ textAlign: "center", marginTop: "2rem" }}>
        <Link href="/cart" className={`${styles.viewCartButton}`}>
          {t("view_cart_checkout_button")}
        </Link>
      </div>

      {/* Floating Cart Button */}
      <FloatingCartButton
        itemCount={itemCount}
        totalPrice={cartTotal}
        onAnimate={cartAnimationTrigger}
      />
    </main>
  );
}
