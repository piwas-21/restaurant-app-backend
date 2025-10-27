"use client";

import React from "react";
import { useTranslation } from "react-i18next";
import { Plus, Minus } from "lucide-react";
import type { SuggestedSideItem } from "@/types/menu";
import styles from "./SuggestedSideItemsSection.module.css";

interface SuggestedSideItemsSectionProps {
  sideItems: SuggestedSideItem[];
  selectedSideItems: Array<{ id: string; quantity: number }>;
  onSelectionChange: (selected: Array<{ id: string; quantity: number }>) => void;
  currentLanguage: string;
}

export default function SuggestedSideItemsSection({
  sideItems,
  selectedSideItems,
  onSelectionChange,
}: SuggestedSideItemsSectionProps) {
  const { t } = useTranslation();

  if (!sideItems || sideItems.length === 0) {
    return null;
  }

  const handleAdd = (sideItemId: string) => {
    const existing = selectedSideItems.find((item) => item.id === sideItemId);
    if (existing) {
      // Increase quantity
      onSelectionChange(
        selectedSideItems.map((item) =>
          item.id === sideItemId ? { ...item, quantity: item.quantity + 1 } : item
        )
      );
    } else {
      // Add new item
      onSelectionChange([...selectedSideItems, { id: sideItemId, quantity: 1 }]);
    }
  };

  const handleRemove = (sideItemId: string) => {
    const existing = selectedSideItems.find((item) => item.id === sideItemId);
    if (existing) {
      if (existing.quantity > 1) {
        // Decrease quantity
        onSelectionChange(
          selectedSideItems.map((item) =>
            item.id === sideItemId ? { ...item, quantity: item.quantity - 1 } : item
          )
        );
      } else {
        // Remove item
        onSelectionChange(selectedSideItems.filter((item) => item.id !== sideItemId));
      }
    }
  };

  const getQuantity = (sideItemId: string) => {
    const item = selectedSideItems.find((item) => item.id === sideItemId);
    return item?.quantity || 0;
  };

  return (
    <div className={styles.section}>
      <h3 className={styles.sectionTitle}>{t("product_suggested_sides")}</h3>
      <p className={styles.sectionDescription}>{t("select_options")}</p>

      <div className={styles.sideItemsList}>
        {sideItems.map((sideItem) => {
          const quantity = getQuantity(sideItem.id);
          const isSelected = quantity > 0;

          return (
            <div key={sideItem.id} className={styles.sideItem}>
              <div className={styles.sideItemInfo}>
                <h4 className={styles.sideItemName}>{sideItem.name}</h4>
                {sideItem.description && (
                  <p className={styles.sideItemDescription}>{sideItem.description}</p>
                )}
                <span className={styles.sideItemPrice}>
                  CHF {sideItem.price.toFixed(2)}
                </span>
              </div>

              <div className={styles.sideItemActions}>
                {isSelected ? (
                  <div className={styles.quantityControl}>
                    <button
                      onClick={() => handleRemove(sideItem.id)}
                      className={styles.quantityButton}
                      aria-label={t("decrease_quantity")}
                      type="button"
                    >
                      <Minus size={16} />
                    </button>
                    <span className={styles.quantity}>{quantity}</span>
                    <button
                      onClick={() => handleAdd(sideItem.id)}
                      className={styles.quantityButton}
                      aria-label={t("increase_quantity")}
                      type="button"
                    >
                      <Plus size={16} />
                    </button>
                  </div>
                ) : (
                  <button
                    onClick={() => handleAdd(sideItem.id)}
                    className={styles.addButton}
                    type="button"
                  >
                    {t("add_ingredient")}
                  </button>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
