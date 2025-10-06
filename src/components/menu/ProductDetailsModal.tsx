"use client";

import React, { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import styles from "@/app/styles/MenuPage.module.css";
import type { MenuItem as MenuItemType, DetailedProduct } from "@/types/menu";
import { useTranslation } from "react-i18next";
import { getProductById } from "@/services/menuService";
import AllergenDisplay from "@/components/common/AllergenDisplay";

type Props = {
  isOpen: boolean;
  item: MenuItemType | null;
  onClose: () => void;
};

export default function ProductDetailsModal({ isOpen, item, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const currentLanguage = (i18n.language.split("-")[0] || "en");

  const [detailedProduct, setDetailedProduct] = useState<DetailedProduct | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Fetch detailed product data when modal opens
  useEffect(() => {
    if (!isOpen || !item) {
      setDetailedProduct(null);
      setError(null);
      return;
    }

    const fetchProductDetails = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const response = await getProductById(item.id);
        if (response.success && response.data) {
          setDetailedProduct(response.data);
        } else {
          throw new Error(response.message || "Failed to fetch product details");
        }
      } catch (err: any) {
        console.error("Failed to fetch product details:", err);
        setError(err.message || "Failed to load product details");
        // Fallback to using the item data from the list
        setDetailedProduct(null);
      } finally {
        setIsLoading(false);
      }
    };

    fetchProductDetails();
  }, [isOpen, item]);

  if (!isOpen || !item) return null;

  // Use detailed product data if available, otherwise fallback to menu item data
  const productData = detailedProduct || item;
  const title = detailedProduct
    ? (detailedProduct.content?.[currentLanguage]?.name || detailedProduct.content?.en?.name || detailedProduct.name)
    : (item.content?.[currentLanguage]?.name || item.content?.en?.name || item.name);
  const description = detailedProduct?.description || item.longDescription || "";
  const price = detailedProduct?.basePrice || (typeof item.price === 'number' ? item.price : parseFloat(item.price as any));

  // For ingredients, use the detailed product ingredients array if available
  const ingredients = detailedProduct?.ingredients ||
    item.ingredients ||
    (item.content?.[currentLanguage]?.description || item.content?.en?.description || "")
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
          {isLoading && (
            <div className={styles.productDetailSection}>
              <p>{t('loading', 'Loading detailed information...')}</p>
            </div>
          )}

          {error && (
            <div className={styles.productDetailSection}>
              <p className={styles.errorMessage}>{error}</p>
            </div>
          )}

          {description && (
            <div className={styles.productDetailSection}>
              <h4>{t('description', 'Description')}</h4>
              <p>{description}</p>
            </div>
          )}

          {ingredients.length > 0 && (
            <div className={styles.productDetailSection}>
              <h4>{t('ingredients')}:</h4>
              <div className={styles.allergyTags}>
                {ingredients.map((ingredient, idx) => (
                  <span key={`${item.id}-ing-full-${idx}`} className={styles.allergyTag}>{ingredient}</span>
                ))}
              </div>
            </div>
          )}

          <div className={styles.productDetailSection} style={{ justifyItems: 'flex-start' }}>
            <h4>{t('allergens', 'Allergens')}:</h4>
            <AllergenDisplay
              allergens={detailedProduct?.allergens || item.allergens}
              id={`${item.id}-modal`}
              variant="admin"
              showLabel={false}
            />
          </div>

          {(detailedProduct?.preparationTimeMinutes || item.preparationTimeMinutes) && (
            <div className={styles.productDetailSection}>
              <h4>{t('preparation_time', 'Preparation Time')}:</h4>
              <p className={styles.preparationTime}>
                <span className={styles.timeIcon}>⏱️</span>
                {detailedProduct?.preparationTimeMinutes || item.preparationTimeMinutes} {t('minutes', 'minutes')}
              </p>
            </div>
          )}

          {detailedProduct?.variations && detailedProduct.variations.length > 0 && (
            <div className={styles.productDetailSection}>
              <h4>{t('variations', 'Variations')}:</h4>
              <div className={styles.variationsList}>
                {detailedProduct.variations
                  .filter(v => v.isActive)
                  .sort((a, b) => a.displayOrder - b.displayOrder)
                  .map((variation, idx) => (
                    <div key={`${item.id}-variation-${idx}`} className={styles.variationItem}>
                      <span className={styles.variationName}>{variation.name}</span>
                      <span className={styles.variationPrice}>
                        CHF {variation.finalPrice.toFixed(2)}
                        {variation.priceModifier !== 0 && (
                          <span className={styles.priceModifier}>
                            {variation.priceModifier > 0
                              ? ` (+${variation.priceModifier.toFixed(2)})`
                              : ` (${variation.priceModifier.toFixed(2)})`
                            }
                          </span>
                        )}
                      </span>
                      {variation.description && (
                        <p className={styles.variationDescription}>{variation.description}</p>
                      )}
                    </div>
                  ))}
              </div>
            </div>
          )}

          {detailedProduct?.suggestedSideItems && detailedProduct.suggestedSideItems.length > 0 && (
            <div className={styles.productDetailSection}>
              <h4>{t('suggested_sides', 'Suggested Side Items')}:</h4>
              <div className={styles.sideItemsList}>
                {detailedProduct.suggestedSideItems
                  .sort((a, b) => a.displayOrder - b.displayOrder)
                  .map((sideItem, idx) => (
                    <div key={`${item.id}-side-${idx}`} className={styles.sideItemCard}>
                      <div className={styles.sideItemInfo}>
                        <span className={styles.sideItemName}>
                          {sideItem.name}
                          {sideItem.isRequired && <span className={styles.requiredBadge}>{t('required', '*')}</span>}
                        </span>
                        <span className={styles.sideItemPrice}>CHF {sideItem.price.toFixed(2)}</span>
                      </div>
                      {sideItem.description && (
                        <p className={styles.sideItemDescription}>{sideItem.description}</p>
                      )}
                    </div>
                  ))}
              </div>
            </div>
          )}

          <div className={styles.productDetailSection}>
            <p className={styles.itemPrice}>CHF {price.toFixed(2)}</p>
          </div>
        </div>
      </div>
    </div>,
    document.body
  );
}
