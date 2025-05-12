// src/app/menu/page.tsx
"use client";

import React, { useState } from "react"; // Added useState
import styles from "../styles/MenuPage.module.css";
import { useCart } from "@/components/cart/CartContext";
import Link from "next/link";
import { useTranslation } from "react-i18next"; // Import useTranslation
import FeedbackForm from "@/components/feedback/FeedbackForm"; // Import FeedbackForm
import AverageRating from "@/components/feedback/AverageRating"; // Import AverageRating
// import toast from "react-hot-toast"; // Comment out react-hot-toast
import { useSnackbar } from "notistack"; // Import useSnackbar from notistack

// Placeholder for fetching/storing feedback - in a real app, this would be a backend API
const mockFeedbackStore: { [dishId: string]: { rating: number; comment?: string; name?: string }[] } = {};
const mockAverageRatings: { [dishId: string]: { average: number; count: number } } = {};

// Function to simulate submitting feedback
const submitFeedbackToStore = async (dishId: string, rating: number, comment?: string, name?: string) => {
  if (!mockFeedbackStore[dishId]) {
    mockFeedbackStore[dishId] = [];
  }
  mockFeedbackStore[dishId].push({ rating, comment, name });

  // Recalculate average rating
  const ratings = mockFeedbackStore[dishId].map(f => f.rating);
  const average = ratings.reduce((acc, curr) => acc + curr, 0) / ratings.length;
  mockAverageRatings[dishId] = { average: parseFloat(average.toFixed(1)), count: ratings.length };
  console.log("Updated Feedback Store:", mockFeedbackStore);
  console.log("Updated Average Ratings:", mockAverageRatings);
  return { success: true }; 
};

// Placeholder menu data - this will eventually come from a database/API
const menuData = {
  categories: [
    {
      id: "starters",
      name_key: "starters_category", // Key for translation
      items: [
        {
          id: "sarma",
          name_key: "sarma_name",
          description_key: "sarma_description",
          price: "1.90",
          image_url: "/images/placeholder-sarma.jpeg",
          allergy_labels: ["Vegan", "Gluten-Free"],
        },
        {
          id: "falafel",
          name_key: "falafel_name",
          description_key: "falafel_description",
          price: "1.50",
          image_url: "/images/placeholder-falafel.jpeg",
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
          image_url: "/images/placeholder-adana.jpeg",
          allergy_labels: ["Halal"],
        },
         {
          id: "iskender_kebab_special",
          name_key: "iskender_kebab_special_name",
          description_key: "iskender_kebab_special_description",
          price: "28.50",
          image_url: "/images/placeholder-iskender.png", // Add a placeholder image
          allergy_labels: ["Halal", "Contains Dairy"],
        },
      ],
    },
  ],
};

export default function MenuPage() {
  const { dispatch } = useCart();
  const { t } = useTranslation(); // Get i18n instance for current language
  const { enqueueSnackbar } = useSnackbar(); // Get enqueueSnackbar from notistack
  const [showFeedbackForm, setShowFeedbackForm] = useState<string | null>(null); // dishId or null
  const [ratings, setRatings] = useState(mockAverageRatings); // Local state for ratings to trigger re-render

  interface MenuItem {
    id: string;
    name_key: string;
    description_key: string;
    price: string;
    image_url: string;
    allergy_labels: string[];
  }

  interface AddItemPayload {
    id: string;
    name: string;
    price: number;
    quantity: number;
  }

  const handleAddItemToCart = (item: MenuItem): void => {
    dispatch({ 
      type: "ADD_ITEM", 
      payload: { 
        id: item.id, 
        name: t(item.name_key), // Use translated name
        price: parseFloat(item.price), 
        quantity: 1 
      } as AddItemPayload
    });
    // toast.success(t("item_added_to_cart_toast", { itemName: t(item.name_key) })); // Old react-hot-toast
    enqueueSnackbar(t("item_added_to_cart_toast", { itemName: t(item.name_key) }), { variant: "success" }); // New notistack notification
  };

  const handleFeedbackSuccess = async (dishId: string) => {
    console.log("Feedback submitted successfully for dishId:", dishId);
    setShowFeedbackForm(null); // Close form
    // Simulate fetching updated ratings - in a real app, this might re-fetch or rely on optimistic updates
    // For this mock, we directly update the local state from the mock store
    setRatings({ ...mockAverageRatings }); 
  };

  return (
    <main className={styles.menuContainer} aria-labelledby="menu-page-heading">
      <h1 id="menu-page-heading">{t("menu_title")}</h1>
      {menuData.categories.map((category) => (
        <section key={category.id} className={styles.categorySection} aria-labelledby={`category-heading-${category.id}`}>
          <h2 id={`category-heading-${category.id}`}>{t(category.name_key)}</h2>
          <div className={styles.itemsGrid} role="list">
            {category.items.map((item) => (
              <div key={item.id} className={styles.menuItem} role="listitem" aria-labelledby={`item-name-${item.id}`}>
                <img src={item.image_url} alt={t(item.name_key)} className={styles.itemImage} />
                <h3 id={`item-name-${item.id}`}>{t(item.name_key)}</h3>
                <p className={styles.itemDescription}>{t(item.description_key)}</p>
                <p className={styles.itemPrice} aria-label={`${t("checkout_total_label")} CHF ${item.price}`}>CHF {item.price}</p>
                
                <AverageRating dishId={item.id} initialRatingData={ratings[item.id]} />

                {item.allergy_labels && item.allergy_labels.length > 0 && (
                  <div className={styles.allergyTags} aria-label={t("dietary_information_label")}>
                    {item.allergy_labels.map((label) => (
                      // Ideally, allergy labels should also be translatable if they are not universal terms
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
                  className={styles.feedbackButton} // Add a new style for this button
                  onClick={() => setShowFeedbackForm(item.id)}
                  aria-label={`${t("feedback_form_heading")} ${t(item.name_key)}`}
                >
                  {t("feedback_form_heading")}
                </button>
                {showFeedbackForm === item.id && (
                  <FeedbackForm 
                    dishId={item.id} 
                    onSubmitSuccess={() => {
                        // Simulate API call to our mock store
                        // In a real app, FeedbackForm would handle its own submission logic
                        // and then call onSubmitSuccess. For this mock, we trigger it here.
                        // This is a simplified approach for demonstration.
                        const feedbackData = mockFeedbackStore[item.id]?.[mockFeedbackStore[item.id].length -1]; // Get latest feedback
                        if(feedbackData) {
                            submitFeedbackToStore(item.id, feedbackData.rating, feedbackData.comment, feedbackData.name)
                                .then(() => handleFeedbackSuccess(item.id));
                        } else {
                             handleFeedbackSuccess(item.id); // Fallback if form closed before submit
                        }
                    }}
                  />
                )}
              </div>
            ))}
          </div>
        </section>
      ))}
      <div style={{textAlign: "center", marginTop: "2rem"}}>
        <Link href="/checkout" className={styles.addToOrderButton} style={{backgroundColor: "#d9534f", padding: "1rem 2rem"}}>
            {t("view_cart_checkout_button")}
        </Link>
      </div>
    </main>
  );
}

