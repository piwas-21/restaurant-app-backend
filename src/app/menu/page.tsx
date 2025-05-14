
"use client";

import React, { useState, useEffect, useCallback } from "react";
import styles from "../styles/MenuPage.module.css";
import { useCart } from "@/components/cart/CartContext";
import Link from "next/link";
import { useTranslation } from "react-i18next";
import FeedbackForm from "@/components/feedback/FeedbackForm";
import AverageRating from "@/components/feedback/AverageRating";
import { useSnackbar } from "notistack";

import categoriesData from "../../data/categories.json";
import type { LanguageCode } from "@/components/LanguageSwitcher";

interface MenuItemContent {
  name: string;
  description: string;
}

type DietaryTag = "vegan" | "halal" | "gluten-free" | "vegetarian" | string;

interface MenuItemImage {
  url: string;
  alt: string;
}

export interface MenuItem {
  id: string;
  content: Partial<Record<LanguageCode, MenuItemContent>> & { en?: MenuItemContent }; 
  price: number; 
  image: string; 
  dietaryTags: DietaryTag[];
  categoryKey: MenuCategoryKey; 
  isSpecial?: boolean;
  images?: MenuItemImage[];
}

const mockFeedbackStore: { [dishId: string]: { rating: number; comment?: string; name?: string }[] } = {};
const mockAverageRatings: { [dishId: string]: { average: number; count: number } } = {};

const submitFeedbackToStore = async (dishId: string, rating: number, comment?: string, name?: string) => {
  if (!mockFeedbackStore[dishId]) {
    mockFeedbackStore[dishId] = [];
  }
  mockFeedbackStore[dishId].push({ rating, comment, name });
  const ratings = mockFeedbackStore[dishId].map(f => f.rating);
  const average = ratings.reduce((acc, curr) => acc + curr, 0) / ratings.length;
  mockAverageRatings[dishId] = { average: parseFloat(average.toFixed(1)), count: ratings.length };
  return { success: true };
};

interface CategoryTranslations {
  [lang: string]: Record<string, string>;
}

// Ensure 'specialOfTheDay' is a valid key type
type TempCategoriesData = typeof categoriesData.en;
type MenuCategoryKey = keyof TempCategoriesData | "specialOfTheDay";

const ALL_ITEMS_KEY = "all" as const;
const SPECIAL_OF_THE_DAY_KEY = "specialOfTheDay" as const;

export default function MenuPage() {
  const { dispatch } = useCart();
  const { t, i18n } = useTranslation();
  const { enqueueSnackbar } = useSnackbar();

  const [categoriesForNav, setCategoriesForNav] = useState<MenuCategoryKey[]>([]);
  // Default selectedView to SPECIAL_OF_THE_DAY_KEY
  const [selectedView, setSelectedView] = useState<MenuCategoryKey | typeof ALL_ITEMS_KEY | null>(SPECIAL_OF_THE_DAY_KEY);
  const [currentMenuItems, setCurrentMenuItems] = useState<MenuItem[]>([]);
  const [isLoadingItems, setIsLoadingItems] = useState(false);
  const [errorLoadingItems, setErrorLoadingItems] = useState<string | null>(null);

  const [showFeedbackForm, setShowFeedbackForm] = useState<string | null>(null);
  const [ratings, setRatings] = useState(mockAverageRatings);
  const [enlargedImageItem, setEnlargedImageItem] = useState<MenuItem | null>(null);
  const [currentImageIndex, setCurrentImageIndex] = useState(0);
  const [isMounted, setIsMounted] = useState(false);

  const currentLanguage = (i18n.language.split('-')[0] || 'en') as LanguageCode;

  useEffect(() => {
    setIsMounted(true);
    // Ensure all keys, including specialOfTheDay, are loaded for navigation
    const loadedCategoryKeys = Object.keys((categoriesData as CategoryTranslations).en) as MenuCategoryKey[];
    setCategoriesForNav(loadedCategoryKeys);
    // The default view is already set to SPECIAL_OF_THE_DAY_KEY
    // If specialOfTheDay wasn't in categories.json for some reason (it is now),
    // and selectedView was null, you might add fallback logic here, but it should be fine.
    if (!selectedView && loadedCategoryKeys.length > 0) {
        setSelectedView(SPECIAL_OF_THE_DAY_KEY); // Ensure default if somehow still null
    }

  }, []); // selectedView removed from deps to prevent re-setting default from user selection

  useEffect(() => {
    if (!selectedView) {
      setCurrentMenuItems([]);
      return;
    }

    const fetchMenuItems = async () => {
      setIsLoadingItems(true);
      setErrorLoadingItems(null);
      setCurrentMenuItems([]);
      let allFetchedItems: MenuItem[] = [];

      try {
        if (selectedView === ALL_ITEMS_KEY) {
          for (const catKey of categoriesForNav) {
            if (catKey === SPECIAL_OF_THE_DAY_KEY && !(categoriesData as CategoryTranslations).en[SPECIAL_OF_THE_DAY_KEY]) {
                // If "all" is selected and specialOfTheDay isn't a formal category in JSON 
                // but we want to load its file anyway (should not happen with current setup)
                // console.log("Skipping specialOfTheDay in 'All' as it might be loaded separately or is dynamic");
                // continue;
            }
            try {
              // Dynamically import JSON for each category
              const categoryModule = await import(`../../data/menu/${catKey}.json`);
              const items: MenuItem[] = categoryModule.default;
              if (Array.isArray(items)) {
                allFetchedItems.push(...items.map(item => ({ ...item, categoryKey: catKey })));
              } else {
                console.warn(`Data for category ${catKey} is not an array.`);
              }
            } catch (catErr) {
              console.warn(`Could not load items for category ${catKey} in 'All' view:`, catErr);
            }
          }
        } else {
          // This handles specific categories including "specialOfTheDay"
          const categoryModule = await import(`../../data/menu/${selectedView}.json`);
          const items: MenuItem[] = categoryModule.default;
          if (!Array.isArray(items)) {
            console.error("Loaded menu data is not an array:", items);
            throw new Error(`Menu data for ${selectedView} is not in the expected format.`);
          }
          allFetchedItems = items.map(item => ({ ...item, categoryKey: selectedView }));
        }
        setCurrentMenuItems(allFetchedItems);
      } catch (err) {
        console.error(`Failed to load menu items for ${selectedView}:`, err);
        const errorMsgKey = selectedView === ALL_ITEMS_KEY ? "error_loading_all_menu_items" : "error_loading_menu_items";
        setErrorLoadingItems(t(errorMsgKey, { categoryName: selectedView }));
        setCurrentMenuItems([]);
      }
      setIsLoadingItems(false);
    };

    if (isMounted) { // Ensure component is mounted before fetching
        fetchMenuItems();
    }
  }, [selectedView, categoriesForNav, t, isMounted]); // Added isMounted

  interface AddItemPayload {
    id: string;
    name: string;
    price: number;
    quantity: number;
  }

  const handleAddItemToCart = useCallback((item: MenuItem): void => {
    const itemName = item.content?.[currentLanguage]?.name || item.content?.en?.name || item.id;
    const numericPrice = typeof item.price === 'string' ? parseFloat(item.price) : item.price;

    dispatch({
      type: "ADD_ITEM",
      payload: {
        id: item.id,
        name: itemName,
        price: numericPrice,
        quantity: 1
      } as AddItemPayload
    });
    enqueueSnackbar(t("item_added_to_cart_toast", { itemName }), { variant: "success" });
  }, [dispatch, enqueueSnackbar, t, currentLanguage]);

  const handleFeedbackSuccess = useCallback(async (dishId: string) => {
    console.log(`Feedback submitted for dish ID: ${dishId}`);
    setShowFeedbackForm(null);
    setRatings({ ...mockAverageRatings });
  }, []);

  const handleImageClick = useCallback((item: MenuItem, imageIndex: number = 0) => {
    setEnlargedImageItem(item);
    const initialImageIndex = item.images && item.images.length > imageIndex ? imageIndex : 0;
    setCurrentImageIndex(initialImageIndex);
  }, []);

  const handleCloseEnlargedImage = useCallback(() => {
    setEnlargedImageItem(null);
    setCurrentImageIndex(0);
  }, []);

  const getEnlargedImages = useCallback(() => {
    if (!enlargedImageItem) return [];
    if (enlargedImageItem.images && enlargedImageItem.images.length > 0) {
      return enlargedImageItem.images;
    }
    const altText = enlargedImageItem.content?.[currentLanguage]?.name || enlargedImageItem.content?.en?.name || enlargedImageItem.id;
    return [{ url: enlargedImageItem.image, alt: altText }];
  }, [enlargedImageItem, currentLanguage]);

  const currentEnlargedGalleryImages = getEnlargedImages();

  const showNextImage = useCallback(() => {
    setCurrentImageIndex((prevIndex) => (prevIndex + 1) % currentEnlargedGalleryImages.length);
  }, [currentEnlargedGalleryImages]);

  const showPrevImage = useCallback(() => {
    setCurrentImageIndex((prevIndex) => (prevIndex - 1 + currentEnlargedGalleryImages.length) % currentEnlargedGalleryImages.length);
  }, [currentEnlargedGalleryImages]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (!enlargedImageItem) return;
      if (event.key === "ArrowRight" && currentEnlargedGalleryImages.length > 1) showNextImage();
      if (event.key === "ArrowLeft" && currentEnlargedGalleryImages.length > 1) showPrevImage();
      if (event.key === "Escape") handleCloseEnlargedImage();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [enlargedImageItem, showNextImage, showPrevImage, handleCloseEnlargedImage, currentEnlargedGalleryImages]);

  if (!isMounted || !selectedView) { // Check for isMounted before rendering actual content
    // Optional: return a loading skeleton or null to prevent flash of unstyled/default content
    return null; 
  }

  const categoryDisplayName = selectedView === ALL_ITEMS_KEY 
    ? t("all_categories_nav") 
    // Ensure categoriesData keys are accessed safely, especially with the new type for MenuCategoryKey
    : (categoriesData as CategoryTranslations)[currentLanguage]?.[selectedView as string] || (categoriesData as CategoryTranslations).en[selectedView as string] || selectedView;

  return (
    <main className={styles.menuContainer} aria-labelledby="menu-page-heading">
      <h1 id="menu-page-heading" className={styles.pageTitle}>{t("menu_title")}</h1>

      {categoriesForNav.length > 0 && (
        <nav className={styles.stickyNav} aria-label={t("category_navigation_label")}>
          {/* Button for "Special of the Day" - Render it first if it exists */}
          {(categoriesData as CategoryTranslations).en[SPECIAL_OF_THE_DAY_KEY] && (
            <button
              key={SPECIAL_OF_THE_DAY_KEY}
              className={`${styles.navButton} ${selectedView === SPECIAL_OF_THE_DAY_KEY ? styles.navButtonActive : ""}`}
              onClick={() => setSelectedView(SPECIAL_OF_THE_DAY_KEY)}
              aria-pressed={selectedView === SPECIAL_OF_THE_DAY_KEY}
            >
              {(categoriesData as CategoryTranslations)[currentLanguage]?.[SPECIAL_OF_THE_DAY_KEY] || (categoriesData as CategoryTranslations).en[SPECIAL_OF_THE_DAY_KEY]}
            </button>
          )}
          {/* Button for "All Items" */}
          <button
            key={ALL_ITEMS_KEY}
            className={`${styles.navButton} ${selectedView === ALL_ITEMS_KEY ? styles.navButtonActive : ""}`}
            onClick={() => setSelectedView(ALL_ITEMS_KEY)}
            aria-pressed={selectedView === ALL_ITEMS_KEY}
          >
            {t("all_categories_nav")}
          </button>
          {/* Other category buttons */}
          {categoriesForNav
            .filter(catKey => catKey !== SPECIAL_OF_THE_DAY_KEY) // Don't repeat "Special of the Day"
            .map((catKey) => {
              const categoryName = (categoriesData as CategoryTranslations)[currentLanguage]?.[catKey as string] || (categoriesData as CategoryTranslations).en[catKey as string] || catKey;
              return (
                <button
                  key={catKey}
                  className={`${styles.navButton} ${selectedView === catKey ? styles.navButtonActive : ""}`}
                  onClick={() => setSelectedView(catKey)}
                  aria-pressed={selectedView === catKey}
                >
                  {categoryName}
                </button>
              );
          })}
        </nav>
      )}

      <section className={styles.categorySection} aria-labelledby={`category-heading-${selectedView}`}>
        <h2 id={`category-heading-${selectedView}`} className={styles.categoryTitle}>{categoryDisplayName}</h2>
        {isLoadingItems && <p>{t("loading_items", "Loading items...")}</p>}
        {errorLoadingItems && <p className={styles.errorMessage}>{errorLoadingItems}</p>}
        {!isLoadingItems && !errorLoadingItems && currentMenuItems.length === 0 && (
          <p>{t("no_items_in_category", { categoryName: categoryDisplayName })}</p>
        )}
        {!isLoadingItems && !errorLoadingItems && currentMenuItems.length > 0 && (
          <div className={styles.itemsGrid} role="list">
            {currentMenuItems.map((item: MenuItem) => {
              const itemName = item.content?.[currentLanguage]?.name || item.content?.en?.name || item.id;
              const itemDescription = item.content?.[currentLanguage]?.description || item.content?.en?.description || "";
              const mainImageAlt = item.content?.[currentLanguage]?.name || item.content?.en?.name || t("menu_item_image_alt") || item.id;
              const numericPrice = typeof item.price === 'string' ? parseFloat(item.price) : item.price;

              return (
                <div key={item.id} className={styles.menuItem} role="listitem" aria-labelledby={`item-name-${item.id}`}>
                  <div className={styles.itemImageContainer} onClick={() => handleImageClick(item, 0)} style={{ cursor: 'pointer' }}>
                    <img 
                      src={item.image} 
                      alt={mainImageAlt} 
                      className={styles.itemImage}
                      onError={(e) => { 
                        (e.target as HTMLImageElement).src = '/images/placeholder-falafel.jpeg'; 
                      }}
                    />
                    {item.images && item.images.length > 1 && (
                      <span className={styles.imageCount}>{item.images.length} {t("images_count_label")}</span>
                    )}
                  </div>
                  <h3 id={`item-name-${item.id}`}>{itemName}</h3>
                  <p className={styles.itemDescription}>{itemDescription}</p>
                  <p className={styles.itemPrice} aria-label={`${t("checkout_total_label")} CHF ${numericPrice.toFixed(2)}`}>CHF {numericPrice.toFixed(2)}</p>
                  
                  <AverageRating dishId={item.id} initialRatingData={ratings[item.id]} />

                  {item.dietaryTags && item.dietaryTags.length > 0 && (
                    <div className={styles.allergyTags} aria-label={t("dietary_information_label")}>
                      {item.dietaryTags.map((tag) => (
                        <span key={tag} className={`${styles.allergyTag} ${styles[tag.toLowerCase().replace(/\s+/g, '-')] || ''}`} role="status">{t(tag, tag)}</span>
                      ))}
                    </div>
                  )}
                  <div className={styles.itemActions}>
                    <button
                      className={styles.addToOrderButton}
                      onClick={() => handleAddItemToCart(item)}
                      aria-label={t("add_item_to_order", { itemName }) }
                    >
                      {t("add_to_order")}
                    </button>
                    <button
                      className={styles.feedbackButton}
                      onClick={() => setShowFeedbackForm(item.id)}
                      aria-label={`${t("feedback_form_heading")} ${itemName}`}
                    >
                      {t("feedback_form_heading")}
                    </button>
                  </div>
                  {showFeedbackForm === item.id && (
                    <FeedbackForm
                      dishId={item.id}
                      onSubmitSuccess={() => {
                          const feedbackData = mockFeedbackStore[item.id]?.[mockFeedbackStore[item.id].length -1];
                          if(feedbackData) {
                              submitFeedbackToStore(item.id, feedbackData.rating, feedbackData.comment, feedbackData.name)
                                  .then(() => handleFeedbackSuccess(item.id));
                          } else {
                               handleFeedbackSuccess(item.id);
                          }
                      }}
                    />
                  )}
                </div>
              );
            })}
          </div>
        )}
      </section>
      
      {enlargedImageItem && currentEnlargedGalleryImages.length > 0 && (
        <div className={styles.enlargedImageBackdrop} onClick={handleCloseEnlargedImage}>
          <div className={styles.enlargedImageModalContainer} onClick={(e) => e.stopPropagation()}>
            <button 
              className={styles.closeButtonModal}
              onClick={handleCloseEnlargedImage}
              aria-label={t("close_image_modal_button", "Close image modal")}
            >
              &times; {/* HTML entity for a multiplication sign (X) */}
            </button>
            {currentEnlargedGalleryImages.length > 1 && (
              <button className={`${styles.navButtonModal} ${styles.prevButton}`} onClick={showPrevImage} aria-label={t("previous_image_button_label")}> 
                &#10094; 
              </button>
            )}
            <img 
              src={currentEnlargedGalleryImages[currentImageIndex].url}
              alt={currentEnlargedGalleryImages[currentImageIndex].alt || `${enlargedImageItem.content?.[currentLanguage]?.name || enlargedImageItem.content?.en?.name || enlargedImageItem.id} - Image ${currentImageIndex + 1}`}
              className={styles.enlargedImageModal}
            />
            {currentEnlargedGalleryImages.length > 1 && (
              <button className={`${styles.navButtonModal} ${styles.nextButton}`} onClick={showNextImage} aria-label={t("next_image_button_label")}> 
                &#10095;
              </button>
            )}
          </div>
        </div>
      )}

      <div style={{textAlign: "center", marginTop: "2rem"}}>
        <Link href="/checkout" className={`${styles.addToOrderButton} ${styles.viewCartButton}`}>
            {t("view_cart_checkout_button")}
        </Link>
      </div>
    </main>
  );
}
