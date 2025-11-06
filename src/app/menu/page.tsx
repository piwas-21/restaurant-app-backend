"use client";

import React, { useState, useEffect } from "react";
import styles from "../styles/MenuPage.module.css";
import Link from "next/link";
import { useTranslation } from "react-i18next";
import TableBanner from "@/components/TableBanner";

import type { LanguageCode } from "@/components/LanguageSwitcher";
import { usePublicMenu, ALL_ITEMS_KEY } from "@/hooks/usePublicMenu";
import { useImageGallery } from "@/hooks/useImageGallery";
import { useFeaturedSpecial } from "@/hooks/useFeaturedSpecial";
import { getCategoryDisplayName } from "@/utils/categoryNameMapper";
import { setFallbackImage } from "@/utils/imageHelpers";

import MenuPageHeader from "@/components/menu/MenuPageHeader";
import MenuContent from "@/components/menu/MenuContent";
import MenuModals from "@/components/menu/MenuModals";
import FeaturedSpecialComponent from "@/components/menu/FeaturedSpecial";

export default function MenuPage() {
  const { t, i18n } = useTranslation();
  const [isMounted, setIsMounted] = useState(false);

  const currentLanguage = (i18n.language.split("-")[0] || "en") as LanguageCode;

  // Custom hooks
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

  const {
    enlargedImageItem,
    currentImageIndex,
    currentEnlargedGalleryImages,
    handleImageClick,
    handleCloseEnlargedImage,
    showNextImage,
    showPrevImage,
  } = useImageGallery(currentLanguage);

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

  useEffect(() => {
    setIsMounted(true);
  }, []);

  if (!isMounted || !selectedView) {
    return null;
  }

  // Get display name for selected category
  const categoryDisplayName =
    selectedView === ALL_ITEMS_KEY
      ? t("all_categories_nav")
      : (() => {
          const category = categoriesForNav.find((c) => c.id === selectedView);
          if (!category) return String(selectedView);
          return getCategoryDisplayName(category.name, t);
        })();

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
        currentPage={currentPage}
        totalPages={totalPages}
        totalCount={totalCount}
        onPageChange={onPageChange}
        onImageClick={handleImageClick}
        getFallbackImage={setFallbackImage}
      />

      <MenuModals
        enlargedImageItem={enlargedImageItem}
        currentImageIndex={currentImageIndex}
        currentEnlargedGalleryImages={currentEnlargedGalleryImages}
        onCloseEnlargedImage={handleCloseEnlargedImage}
        onNextImage={showNextImage}
        onPrevImage={showPrevImage}
        currentLanguage={currentLanguage}
        featuredSpecial={featuredSpecial}
        showFeaturedDetails={showFeaturedDetails}
        showFeaturedCustomization={showFeaturedCustomization}
        onCloseFeaturedDetails={handleCloseFeaturedDetails}
        onCloseFeaturedCustomization={() => setShowFeaturedCustomization(false)}
        onFeaturedCustomizationConfirm={handleFeaturedCustomizationConfirm}
      />

      <div style={{ textAlign: "center", marginTop: "2rem" }}>
        <Link href="/cart" className={`${styles.viewCartButton}`}>
          {t("view_cart_checkout_button")}
        </Link>
      </div>
    </main>
  );
}
