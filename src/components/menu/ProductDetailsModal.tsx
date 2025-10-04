"use client";

import React from "react";
import { createPortal } from "react-dom";
import styles from "@/app/styles/MenuPage.module.css";
import type { MenuItem as MenuItemType } from "@/types/menu";
import { useTranslation } from "react-i18next";

type Props = {
  isOpen: boolean;
  item: MenuItemType | null;
  onClose: () => void;
};

export default function ProductDetailsModal({ isOpen, item, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const currentLanguage = (i18n.language.split("-")[0] || "en");

  if (!isOpen || !item) return null;

  const title = item.content?.[currentLanguage]?.name || item.content?.en?.name || item.id;
  const description = item.longDescription || "";
  const ingredientsText = item.content?.[currentLanguage]?.description || item.content?.en?.description || "";
  const price = typeof item.price === 'number' ? item.price : parseFloat(item.price as any);

  const ingredients = ingredientsText
    .split(/[\,\n;]+/)
    .map(s => s.trim())
    .filter(Boolean);

  return createPortal(
    <div className={styles.productDetailsModal} onClick={onClose}>
      <div className={styles.productDetailsContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.productDetailsHeader}>
          <h3>{title}</h3>
          <button className={styles.productDetailsClose} onClick={onClose} aria-label={t('close')}>×</button>
        </div>
        <div className={styles.productDetailsBody}>
          {description && <p>{description}</p>}
          {ingredients.length > 0 && (
            <div className={styles.allergyTags} aria-label={t('ingredients')}>
              <span className={styles.ingredientsLabel}>{t('ingredients')}:</span>
              {ingredients.map((p, idx) => (
                <span key={`${item.id}-ing-full-${idx}`} className={styles.allergyTag}>{p}</span>
              ))}
            </div>
          )}
          <p className={styles.itemPrice}>CHF {price.toFixed(2)}</p>
        </div>
      </div>
    </div>,
    document.body
  );
}
