import React from 'react';
import { useTranslation } from 'react-i18next';
import type { MenuItem, ApiCategory } from '@/types/menu';
import { ALL_ITEMS_KEY } from '@/hooks/usePublicMenu';
import CategoryNav from '@/components/menu/CategoryNav';
import MenuList from '@/components/menu/MenuList';
import Pagination from '@/components/common/Pagination';
import styles from '@/app/styles/MenuPage.module.css';

interface MenuContentProps {
  categoriesForNav: ApiCategory[];
  selectedView: string | typeof ALL_ITEMS_KEY;
  onSelectView: (view: string | typeof ALL_ITEMS_KEY) => void;
  categoryDisplayName: string;
  isLoadingItems: boolean;
  errorLoadingItems: string | null;
  currentMenuItems: MenuItem[];
  currentPage: number;
  totalPages: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  onImageClick: (item: MenuItem, imageIndex?: number) => void;
  getFallbackImage: (menuItem: MenuItem) => void;
}

export default function MenuContent({
  categoriesForNav,
  selectedView,
  onSelectView,
  categoryDisplayName,
  isLoadingItems,
  errorLoadingItems,
  currentMenuItems,
  currentPage,
  totalPages,
  totalCount,
  onPageChange,
  onImageClick,
  getFallbackImage,
}: MenuContentProps) {
  const { t } = useTranslation();

  const displayError = errorLoadingItems
    ? t(
        selectedView === ALL_ITEMS_KEY
          ? "error_loading_all_menu_items"
          : "error_loading_menu_items",
        { categoryName: categoryDisplayName }
      )
    : null;

  return (
    <>
      {/* Category Navigation */}
      {categoriesForNav.length > 0 && (
        <CategoryNav
          categories={categoriesForNav}
          selectedView={selectedView}
          onSelect={onSelectView}
          allLabel={t("all_categories_nav")}
        />
      )}

      {/* Menu Items Section */}
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

        {/* Loading State */}
        {isLoadingItems && <p>{t("loading_items", "Loading items...")}</p>}

        {/* Error State */}
        {displayError && <p className={styles.errorMessage}>{displayError}</p>}

        {/* Empty State */}
        {!isLoadingItems && !displayError && currentMenuItems.length === 0 && (
          <p>
            {t("no_items_in_category", { categoryName: categoryDisplayName })}
          </p>
        )}

        {/* Menu Items */}
        {!isLoadingItems && !displayError && currentMenuItems.length > 0 && (
          <>
            <MenuList
              items={currentMenuItems}
              onImageClick={onImageClick}
              onFeedbackSuccess={() => {}}
              getFallbackImage={getFallbackImage}
            />

            {/* Pagination */}
            <Pagination
              currentPage={currentPage}
              totalPages={totalPages}
              onPageChange={onPageChange}
              isLoading={isLoadingItems}
            />

            {/* Pagination Info */}
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
    </>
  );
}
