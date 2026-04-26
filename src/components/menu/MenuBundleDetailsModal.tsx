'use client';

import React from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import type { MenuBundleItem } from '@/types/menu';
import type { LanguageCode } from '@/components/LanguageSwitcher';
import AllergenDisplay from '@/components/common/AllergenDisplay';
import styles from './MenuBundleDetailsModal.module.css';

interface MenuBundleDetailsModalProps {
  bundle: MenuBundleItem | null;
  isOpen: boolean;
  onClose: () => void;
  onAddToCart: (bundle: MenuBundleItem) => void;
  currentLanguage: LanguageCode;
}

const MenuBundleDetailsModal: React.FC<MenuBundleDetailsModalProps> = ({
  bundle,
  isOpen,
  onClose,
  onAddToCart,
  currentLanguage,
}) => {
  const { t } = useTranslation();

  // Helper function to get translated ingredient names
  const getIngredientNames = (item: any): string => {
    if (item.detailedIngredients && item.detailedIngredients.length > 0) {
      return item.detailedIngredients
        .map((ing: any) => ing.content?.[currentLanguage]?.name || ing.content?.en?.name || ing.name)
        .join(', ');
    }
    return item.ingredients?.join(', ') || '';
  };

  if (!isOpen || !bundle) return null;

  // Get localized name and description
  const bundleName =
    bundle.content?.[currentLanguage]?.name ||
    bundle.content?.en?.name ||
    bundle.name;

  const bundleDescription =
    bundle.content?.[currentLanguage]?.description ||
    bundle.content?.en?.description ||
    bundle.description ||
    '';

  const handleAddToCart = () => {
    onClose();
    // Small delay to allow modal to close before opening customization
    setTimeout(() => {
      onAddToCart(bundle);
    }, 100);
  };

  return createPortal(
    <div className={styles.modalOverlay} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h3>{bundleName}</h3>
          <button
            className={styles.closeButton}
            onClick={onClose}
            aria-label={t('close')}
          >
            ✕
          </button>
        </div>

        <div className={styles.modalBody}>
          {bundleDescription && (
            <div className={styles.section}>
              <h4>{t('description', 'Description')}</h4>
              <p>{bundleDescription}</p>
            </div>
          )}

          <div className={styles.section}>
            <h4>{t('bundle_includes')}:</h4>
            <div className={styles.bundleSectionsList}>
              {bundle.menuDefinition?.sections?.map((section) => (
                <div key={section.id} className={styles.bundleSectionItem}>
                  <div className={styles.sectionInfo}>
                    <span className={styles.sectionName}>
                      {section.name}
                      {section.isRequired && (
                        <span className={styles.requiredBadge}>{t('required', '*')}</span>
                      )}
                    </span>
                    <span className={styles.sectionMeta}>
                      {t('select_min_max', {
                        min: section.minSelection,
                        max: section.maxSelection,
                      })}
                    </span>
                  </div>

                  <div className={styles.itemsList}>
                    {section.items.map((item) => (
                      <div key={item.id} className={styles.itemRowWrapper}>
                        <div className={styles.itemRow}>
                          <span className={styles.itemName}>
                            {item.productName}
                            {item.isDefault && (
                              <span className={styles.defaultBadge}>{t('default')}</span>
                            )}
                          </span>
                          {item.additionalPrice > 0 && (
                            <span className={styles.itemPrice}>
                              +CHF {item.additionalPrice.toFixed(2)}
                            </span>
                          )}
                        </div>

                        {getIngredientNames(item) && (
                          <div className={styles.itemIngredients}>
                            {getIngredientNames(item)}
                          </div>
                        )}

                        {(item.allergens && item.allergens.length > 0) && (
                          <AllergenDisplay
                            allergens={item.allergens}
                            variant="compact"
                            maxVisible={5}
                            showLabel={true}
                            className={styles.itemAllergens}
                          />
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div className={styles.footer}>
            <div className={styles.priceContainer}>
              <span className={styles.totalPrice}>CHF {bundle.basePrice.toFixed(2)}</span>
            </div>
            <button className={styles.addToCartButton} onClick={handleAddToCart}>
              {t('add_to_order')}
            </button>
          </div>
        </div>
      </div>
    </div>,
    document.body
  );
};

export default MenuBundleDetailsModal;
