// src/app/menu/page.tsx
"use client";

import React, { useState, useEffect, useCallback } from "react"; 
import styles from "../styles/MenuPage.module.css";
import { useCart } from "@/components/cart/CartContext";
import Link from "next/link";
import { useTranslation } from "react-i18next";
import FeedbackForm from "@/components/feedback/FeedbackForm";
import AverageRating from "@/components/feedback/AverageRating";
import { useSnackbar } from "notistack";

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

const menuData = {
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
          image_urls: ["/images/placeholder-sarma.jpeg", "/images/placeholder-falafel.jpeg"], 
          allergy_labels: ["Vegan", "Gluten-Free"],
        },
        {
          id: "falafel",
          name_key: "falafel_name",
          description_key: "falafel_description",
          price: "1.50",
          image_urls: ["/images/placeholder-falafel.jpeg"], 
          allergy_labels: ["Vegan", "Gluten-Free"],
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
          image_urls: ["/images/placeholder-adana.jpeg"],
          allergy_labels: ["Halal"],
        },
         {
          id: "iskender_kebab_special",
          name_key: "iskender_kebab_special_name",
          description_key: "iskender_kebab_special_description",
          price: "28.50",
          image_urls: ["/images/placeholder-falafel.jpeg", "/images/placeholder-adana.jpeg"],
          allergy_labels: ["Halal", "Contains Dairy"],
        },
      ],
    },
  ],
};

export default function MenuPage() {
  const { dispatch } = useCart();
  const { t } = useTranslation();
  const { enqueueSnackbar } = useSnackbar();
  const [showFeedbackForm, setShowFeedbackForm] = useState<string | null>(null);
  const [ratings, setRatings] = useState(mockAverageRatings);
  
  const [enlargedImageItem, setEnlargedImageItem] = useState<MenuItem | null>(null);
  const [currentImageIndex, setCurrentImageIndex] = useState(0);
  const [isMounted, setIsMounted] = useState(false); // State to track client-side mount

  useEffect(() => {
    setIsMounted(true); // Set to true after component mounts on the client
  }, []);

  interface MenuItem {
    id: string;
    name_key: string;
    description_key: string;
    price: string;
    image_urls: string[]; 
    allergy_labels: string[];
  }

  interface AddItemPayload {
    id: string;
    name: string;
    price: number;
    quantity: number;
  }

  const handleAddItemToCart = useCallback((item: MenuItem): void => {
    dispatch({
      type: "ADD_ITEM",
      payload: {
        id: item.id,
        name: t(item.name_key),
        price: parseFloat(item.price),
        quantity: 1
      } as AddItemPayload
    });
    enqueueSnackbar(t("item_added_to_cart_toast", { itemName: t(item.name_key) }), { variant: "success" });
  }, [dispatch, enqueueSnackbar, t]);

  const handleFeedbackSuccess = useCallback(async (dishId: string) => {
    console.log("Feedback submitted successfully for dish ID:", dishId);
    setShowFeedbackForm(null);
    setRatings({ ...mockAverageRatings });
  }, []); 

  const handleImageClick = useCallback((item: MenuItem, index: number = 0) => {
    setEnlargedImageItem(item);
    setCurrentImageIndex(index);
  }, []);

  const handleCloseEnlargedImage = useCallback(() => {
    setEnlargedImageItem(null);
    setCurrentImageIndex(0);
  }, []);

  const showNextImage = useCallback(() => {
    if (enlargedImageItem && enlargedImageItem.image_urls) {
      setCurrentImageIndex((prevIndex) => (prevIndex + 1) % enlargedImageItem.image_urls.length);
    }
  }, [enlargedImageItem]);

  const showPrevImage = useCallback(() => {
    if (enlargedImageItem && enlargedImageItem.image_urls) {
      setCurrentImageIndex((prevIndex) => (prevIndex - 1 + enlargedImageItem.image_urls.length) % enlargedImageItem.image_urls.length);
    }
  }, [enlargedImageItem]);
  
  // Keyboard navigation for enlarged image modal
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (!enlargedImageItem) return;
      if (event.key === "ArrowRight") {
        showNextImage();
      }
      if (event.key === "ArrowLeft") {
        showPrevImage();
      }
      if (event.key === "Escape") {
        handleCloseEnlargedImage();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [enlargedImageItem, showNextImage, showPrevImage, handleCloseEnlargedImage]);

  if (!isMounted) {
    // Render nothing or a fallback loader on the server and initial client render
    return null; 
  }

  return (
    <main className={styles.menuContainer} aria-labelledby="menu-page-heading">
      <h1 id="menu-page-heading">{t("menu_title")}</h1>
      {menuData.categories.map((category) => (
        <section key={category.id} className={styles.categorySection} aria-labelledby={`category-heading-${category.id}`}>
          <h2 id={`category-heading-${category.id}`}>{t(category.name_key)}</h2>
          <div className={styles.itemsGrid} role="list">
            {category.items.map((item: MenuItem) => (
              <div key={item.id} className={styles.menuItem} role="listitem" aria-labelledby={`item-name-${item.id}`}>
                <div style={{ position: 'relative', cursor: 'pointer' }} onClick={() => handleImageClick(item, 0)}>
                  <img 
                    src={item.image_urls[0]}
                    alt={t(item.name_key)} 
                    className={styles.itemImage}
                    style={{ cursor: 'pointer' }}
                  />
                  {item.image_urls.length > 1 && (
                    <span className={styles.imageCount}>{item.image_urls.length} {t("images_count_label")}</span>
                  )}
                </div>
                <h3 id={`item-name-${item.id}`}>{t(item.name_key)}</h3>
                <p className={styles.itemDescription}>{t(item.description_key)}</p>
                <p className={styles.itemPrice} aria-label={`${t("checkout_total_label")} CHF ${item.price}`}>CHF {item.price}</p>
                
                <AverageRating dishId={item.id} initialRatingData={ratings[item.id]} />

                {item.allergy_labels && item.allergy_labels.length > 0 && (
                  <div className={styles.allergyTags} aria-label={t("dietary_information_label")}>
                    {item.allergy_labels.map((label) => (
                      <span key={label} className={styles.allergyTag} role="status">{label}</span>
                    ))}
                  </div>
                )}
                <button
                  className={styles.addToOrderButton}
                  onClick={() => handleAddItemToCart(item)}
                  aria-label={`${t("add_item_to_order", { itemName: t(item.name_key) }) }`}
                >
                  {t("add_to_order")}
                </button>
                <button
                  className={styles.feedbackButton}
                  onClick={() => setShowFeedbackForm(item.id)}
                  aria-label={`${t("feedback_form_heading")} ${t(item.name_key)}`}
                >
                  {t("feedback_form_heading")}
                </button>
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
            ))}
          </div>
        </section>
      ))}
      {enlargedImageItem && enlargedImageItem.image_urls && (
        <div className={styles.enlargedImageBackdrop} onClick={handleCloseEnlargedImage}>
          <div className={styles.enlargedImageModalContainer} onClick={(e) => e.stopPropagation()}>
            {enlargedImageItem.image_urls.length > 1 && (
              <button className={`${styles.navButton} ${styles.prevButton}`} onClick={showPrevImage} aria-label={t("previous_image_button_label")}>
                &#10094; 
              </button>
            )}
            <img 
              src={enlargedImageItem.image_urls[currentImageIndex]} 
              alt={`${t(enlargedImageItem.name_key)} - ${currentImageIndex + 1}`}
              className={styles.enlargedImageModal}
            />
            {enlargedImageItem.image_urls.length > 1 && (
              <button className={`${styles.navButton} ${styles.nextButton}`} onClick={showNextImage} aria-label={t("next_image_button_label")}>
                &#10095;
              </button>
            )}
          </div>
        </div>
      )}
      <div style={{textAlign: "center", marginTop: "2rem"}}>
        <Link href="/checkout" className={styles.addToOrderButton} style={{backgroundColor: "#d9534f", padding: "1rem 2rem"}}>
            {t("view_cart_checkout_button")}
        </Link>
      </div>
    </main>
  );
}
