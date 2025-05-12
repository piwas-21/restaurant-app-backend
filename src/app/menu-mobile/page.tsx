"use client";

import React, { useState, useEffect, useMemo } from "react";
import styles from "../styles/MenuMobile.module.css";
import { useCart } from "@/components/cart/CartContext";
import Link from "next/link";
import Image from "next/image";
import { useTranslation } from "react-i18next";

// Define proper interfaces for our data structure
interface MenuItem {
  id: string;
  name_key: string;
  description_key: string;
  price: string;
  image_url: string;
  allergy_labels_keys: string[];
}

interface MenuCategory {
  id: string;
  name_key: string;
  items: MenuItem[];
}

interface MenuData {
  categories: MenuCategory[];
}

interface FilterOption {
  id: string;
  name_key: string;
}

// Placeholder data - in a real app, this would come from an API
// Added more diverse allergy_labels_keys for testing filters
const initialMenuData: MenuData = {
  categories: [
    {
      id: "starters",
      name_key: "starters_category",
      items: [
        {
          id: "sarma",
          name_key: "sarma_name",
          description_key: "sarma_description",
          price: "1.90",
          image_url: "/placeholder-sarma.jpg",
          allergy_labels_keys: ["filter_vegan", "filter_gluten_free"],
        },
        {
          id: "falafel",
          name_key: "falafel_name",
          description_key: "falafel_description",
          price: "1.50",
          image_url: "/placeholder-falafel.jpg",
          allergy_labels_keys: ["filter_vegan", "filter_gluten_free"],
        },
        {
          id: "mercimek_corbasi",
          name_key: "mercimek_corbasi_name", // Add this key to translation files
          description_key: "mercimek_corbasi_description", // Add this key
          price: "8.50",
          image_url: "/placeholder-mercimek.jpg",
          allergy_labels_keys: ["filter_vegetarian", "filter_gluten_free"],
        },
      ],
    },
    {
      id: "mains",
      name_key: "main_courses_category",
      items: [
        {
          id: "adana_kebab",
          name_key: "adana_kebab_name",
          description_key: "adana_kebab_description",
          price: "23.90",
          image_url: "/placeholder-adana.jpg",
          allergy_labels_keys: ["filter_halal"],
        },
        {
          id: "iskender_kebab",
          name_key: "iskender_kebab_special_name", // Reusing for simplicity
          description_key: "iskender_kebab_special_description",
          price: "29.50",
          image_url: "/placeholder-iskender.jpg",
          allergy_labels_keys: ["filter_halal", "filter_contains_dairy"],
        },
        {
          id: "sebzeli_guvec",
          name_key: "sebzeli_guvec_name", // Add this key
          description_key: "sebzeli_guvec_description", // Add this key
          price: "21.00",
          image_url: "/placeholder-guvec.jpg",
          allergy_labels_keys: ["filter_vegan", "filter_gluten_free"],
        },
      ],
    },
    {
      id: "desserts",
      name_key: "desserts_category",
      items: [
        {
          id: "kunefe",
          name_key: "kunefe_name", // Add this key
          description_key: "kunefe_description", // Add this key
          price: "12.00",
          image_url: "/placeholder-kunefe.jpg",
          allergy_labels_keys: ["filter_vegetarian", "filter_contains_dairy", "filter_contains_nuts"],
        },
      ],
    },
  ],
};

const availableFilters: FilterOption[] = [
  { id: "filter_vegan", name_key: "filter_vegan" },
  { id: "filter_vegetarian", name_key: "filter_vegetarian" },
  { id: "filter_gluten_free", name_key: "filter_gluten_free" },
  { id: "filter_halal", name_key: "filter_halal" },
  { id: "filter_contains_dairy", name_key: "filter_contains_dairy" },
  { id: "filter_contains_nuts", name_key: "filter_contains_nuts" },
];

export default function MenuMobilePage() {
  const { t } = useTranslation();
  const { dispatch, state: cartState } = useCart();
  const [activeCategory, setActiveCategory] = useState<string | null>(initialMenuData.categories[0]?.id || null);
  const [selectedFilters, setSelectedFilters] = useState<string[]>([]);
  const [showFilters, setShowFilters] = useState(false);

  // Fixed type issues with these functions
  const getItemName = (item: MenuItem): string => t(item.name_key);
  const getItemDescription = (item: MenuItem): string => t(item.description_key);

  const handleAddItemToCart = (item: MenuItem): void => {
    dispatch({
      type: "ADD_ITEM",
      payload: {
        id: item.id,
        name: getItemName(item),
        price: parseFloat(item.price),
        quantity: 1,
      },
    });
  };

  const getItemQuantityInCart = (itemId: string): number => {
    const itemInCart = cartState.items.find(cartItem => cartItem.id === itemId);
    return itemInCart ? itemInCart.quantity : 0;
  };

  const totalCartItems = cartState.items.reduce((sum, item) => sum + item.quantity, 0);
  const totalCartPrice = cartState.items.reduce((total, item) => total + item.price * item.quantity, 0).toFixed(2);

  const handleFilterChange = (filterId: string): void => {
    setSelectedFilters(prevFilters =>
      prevFilters.includes(filterId)
        ? prevFilters.filter(f => f !== filterId)
        : [...prevFilters, filterId]
    );
  };

  const resetFilters = (): void => {
    setSelectedFilters([]);
  };

  const filteredMenuData = useMemo((): MenuData => {
    if (selectedFilters.length === 0) {
      return initialMenuData;
    }
    return {
      categories: initialMenuData.categories.map(category => ({
        ...category,
        items: category.items.filter(item =>
          selectedFilters.every(filter => item.allergy_labels_keys.includes(filter))
        ),
      })).filter(category => category.items.length > 0), // Hide category if no items match
    };
  }, [selectedFilters]);

  useEffect(() => {
    // If active category has no items after filtering, switch to first available or null
    const currentCategoryData = filteredMenuData.categories.find(c => c.id === activeCategory);
    if (!currentCategoryData || currentCategoryData.items.length === 0) {
      setActiveCategory(filteredMenuData.categories[0]?.id || null);
    }
  }, [filteredMenuData, activeCategory]);

  return (
    <main className={styles.menuMobileContainer} aria-labelledby="mobile-menu-heading">
      <header className={styles.menuHeader}>
        <Link href="/welcome-mobile" className={styles.backButton} aria-label={t("back_to_welcome")}>
          &larr;
        </Link>
        <h1 id="mobile-menu-heading">{t("menu_title")}</h1>
        <div className={styles.headerActions}>
          <button onClick={() => setShowFilters(!showFilters)} className={styles.filterButton} aria-label={t("filter_by_diet_allergen")} aria-expanded={showFilters}>
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24" fill="currentColor"><path d="M10 18h4v-2h-4v2zM3 6v2h18V6H3zm3 7h12v-2H6v2z"/></svg>
          </button>
          <Link href="/cart-mobile" className={styles.cartLink} aria-label={t("view_cart_items", { count: totalCartItems }) }>
            🛒 <span className={styles.cartCount}>{totalCartItems}</span>
          </Link>
        </div>
      </header>

      {showFilters && (
        <section className={styles.filterSectionMobile} aria-labelledby="filter-heading">
          <h2 id="filter-heading" className={styles.filterTitle}>{t("filter_by_diet_allergen")}</h2>
          <div className={styles.filterOptionsMobile}>
            {availableFilters.map(filter => (
              <button
                key={filter.id}
                onClick={() => handleFilterChange(filter.id)}
                className={`${styles.filterOptionButton} ${selectedFilters.includes(filter.id) ? styles.activeFilter : ""}`}
                aria-pressed={selectedFilters.includes(filter.id)}
              >
                {t(filter.name_key)}
              </button>
            ))}
          </div>
          <button onClick={resetFilters} className={styles.resetFiltersButtonMobile} disabled={selectedFilters.length === 0}>
            {t("filter_reset")}
          </button>
        </section>
      )}

      <nav className={styles.categoryTabs} aria-label={t("menu_categories_label")}>
        {filteredMenuData.categories.map((category) => (
          <button
            key={category.id}
            className={`${styles.categoryTab} ${activeCategory === category.id ? styles.activeTab : ""}`}
            onClick={() => setActiveCategory(category.id)}
            aria-pressed={activeCategory === category.id}
            disabled={category.items.length === 0} // Disable tab if no items after filtering
          >
            {t(category.name_key)} 
          </button>
        ))}
      </nav>

      {filteredMenuData.categories.length === 0 && selectedFilters.length > 0 && (
        <p className={styles.noItemsMatchMessage}>{t("no_items_match_filters")}</p>
      )}

      {filteredMenuData.categories.map((category) => (
        activeCategory === category.id && category.items.length > 0 && (
          <section key={category.id} className={styles.categorySectionMobile} aria-labelledby={`category-heading-${category.id}`}>
            <h2 id={`category-heading-${category.id}`} className="sr-only">{t(category.name_key)}</h2>
            <div className={styles.itemsListMobile} role="list">
              {category.items.map((item) => {
                const quantityInCart = getItemQuantityInCart(item.id);
                const itemName = getItemName(item);
                const itemDescription = getItemDescription(item);
                return (
                  <article key={item.id} className={styles.menuItemMobile} role="listitem" aria-labelledby={`item-name-${item.id}`}>
                    <div className={styles.itemImageContainerMobile}>
                      <Image src={item.image_url} alt={itemName} width={80} height={80} className={styles.itemImageMobile} />
                    </div>
                    <div className={styles.itemDetailsMobile}>
                      <h3 id={`item-name-${item.id}`}>{itemName}</h3>
                      <p className={styles.itemDescriptionMobile}>{itemDescription.substring(0, 70)}...</p>
                      <p className={styles.itemPriceMobile}>CHF {item.price}</p>
                      {item.allergy_labels_keys && item.allergy_labels_keys.length > 0 && (
                        <div className={styles.allergyTagsMobile} aria-label={t("dietary_information_label")}>
                          {item.allergy_labels_keys.map((labelKey) => (
                            <span key={labelKey} className={`${styles.allergyTagMobile} ${styles[labelKey.replace("filter_", "tag_")]}`}>{t(labelKey, labelKey.replace("filter_", "").replace("_", " "))}</span>
                          ))}
                        </div>
                      )}
                    </div>
                    <div className={styles.itemActionsMobile}>
                      {quantityInCart > 0 ? (
                        <div className={styles.quantityControl}>
                          <button 
                            onClick={() => dispatch({ type: "DECREMENT_ITEM", payload: { id: item.id } }) } 
                            className={styles.quantityButton}
                            aria-label={t("decrease_quantity_of_item", { itemName: itemName })}
                          >-</button>
                          <span className={styles.quantityDisplay}>{quantityInCart}</span>
                          <button 
                            onClick={() => handleAddItemToCart(item)} 
                            className={styles.quantityButton}
                            aria-label={t("increase_quantity_of_item", { itemName: itemName })}
                           >+</button>
                        </div>
                      ) : (
                        <button
                          className={styles.addToOrderButtonMobile}
                          onClick={() => handleAddItemToCart(item)}
                          aria-label={t("add_item_to_order", { itemName: itemName })}
                        >
                          {t("add_to_order")}
                        </button>
                      )}
                    </div>
                  </article>
                );
              })}
            </div>
          </section>
        )
      ))}
      {totalCartItems > 0 && (
         <Link href="/cart-mobile" className={styles.viewCartFloatingButton}>
            {t("view_cart_floating_button", { count: totalCartItems, totalPrice: totalCartPrice })}
        </Link>
      )}
    </main>
  );
}
