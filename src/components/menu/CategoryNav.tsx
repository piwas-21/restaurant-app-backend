"use client";

import React, { useRef, useState, useEffect } from "react";
import { useTranslation } from "react-i18next";
import { ChevronLeft, ChevronRight } from "lucide-react";
import styles from "@/app/styles/MenuPage.module.css";
import type { ApiCategory } from "@/types/menu";

type Props = {
  categories: ApiCategory[];
  selectedView: string | typeof ALL_ITEMS_KEY;
  onSelect: (value: string | typeof ALL_ITEMS_KEY) => void;
  allLabel: string;
};

export const ALL_ITEMS_KEY = "all" as const;

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

export default function CategoryNav({ categories, selectedView, onSelect, allLabel }: Props) {
  const { t } = useTranslation();
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const [canScrollLeft, setCanScrollLeft] = useState(false);
  const [canScrollRight, setCanScrollRight] = useState(false);

  const getCategoryDisplayName = (categoryName: string) => {
    const translationKey = mapCategoryNameToTranslationKey(categoryName);
    const translatedName = t(translationKey);

    // If translation exists and is different from the key, use it; otherwise use API name
    return translatedName !== translationKey ? translatedName : categoryName;
  };

  const checkScrollButtons = () => {
    if (scrollContainerRef.current) {
      const { scrollLeft, scrollWidth, clientWidth } = scrollContainerRef.current;
      setCanScrollLeft(scrollLeft > 0);
      setCanScrollRight(scrollLeft < scrollWidth - clientWidth - 1);
    }
  };

  useEffect(() => {
    // Initial check after mount and when categories change
    const timer = setTimeout(() => {
      checkScrollButtons();
    }, 100);

    const container = scrollContainerRef.current;
    if (container) {
      container.addEventListener('scroll', checkScrollButtons);
      window.addEventListener('resize', checkScrollButtons);
      return () => {
        clearTimeout(timer);
        container.removeEventListener('scroll', checkScrollButtons);
        window.removeEventListener('resize', checkScrollButtons);
      };
    }

    return () => clearTimeout(timer);
  }, [categories]);

  const scroll = (direction: 'left' | 'right') => {
    if (scrollContainerRef.current) {
      const scrollAmount = 300;
      const newScrollLeft = direction === 'left'
        ? scrollContainerRef.current.scrollLeft - scrollAmount
        : scrollContainerRef.current.scrollLeft + scrollAmount;

      scrollContainerRef.current.scrollTo({
        left: newScrollLeft,
        behavior: 'smooth'
      });
    }
  };

  // Show maximum 5 categories at once
  const allCategories = [
    { id: ALL_ITEMS_KEY, name: allLabel, isAll: true },
    ...categories.map(cat => ({ id: cat.id, name: getCategoryDisplayName(cat.name), isAll: false }))
  ];

  const showNavArrows = allCategories.length > 5;

  return (
    <nav className={styles.stickyNav} aria-label="Category Navigation">
      <div className={styles.navWrapper}>
        {showNavArrows && canScrollLeft && (
          <button
            className={`${styles.navArrow} ${styles.navArrowLeft}`}
            onClick={() => scroll('left')}
            aria-label="Scroll left"
            type="button"
          >
            <ChevronLeft size={24} />
          </button>
        )}

        <div
          ref={scrollContainerRef}
          className={styles.navScrollContainer}
        >
          <div className={styles.navButtonsContainer}>
            <button
              key={ALL_ITEMS_KEY}
              className={`${styles.navButton} ${selectedView === ALL_ITEMS_KEY ? styles.navButtonActive : ""}`}
              onClick={() => onSelect(ALL_ITEMS_KEY)}
              aria-pressed={selectedView === ALL_ITEMS_KEY}
            >
              {allLabel}
            </button>
            {categories.map((cat) => (
              <button
                key={cat.id}
                className={`${styles.navButton} ${selectedView === cat.id ? styles.navButtonActive : ""}`}
                onClick={() => onSelect(cat.id)}
                aria-pressed={selectedView === cat.id}
              >
                {getCategoryDisplayName(cat.name)}
              </button>
            ))}
          </div>
        </div>

        {showNavArrows && canScrollRight && (
          <button
            className={`${styles.navArrow} ${styles.navArrowRight}`}
            onClick={() => scroll('right')}
            aria-label="Scroll right"
            type="button"
          >
            <ChevronRight size={24} />
          </button>
        )}
      </div>
    </nav>
  );
}

