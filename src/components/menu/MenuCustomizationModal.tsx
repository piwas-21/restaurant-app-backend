'use client';

import React, { useState, useEffect } from 'react';
import styles from './MenuCustomization.module.css';
import { MenuDefinition, MenuSection, SelectedMenuOption } from '@/types/menu';
import { useTranslation } from 'react-i18next';
import ProductCustomizationInBundle, { ProductCustomization } from './ProductCustomizationInBundle';
import AllergenDisplay from '@/components/common/AllergenDisplay';
import type { LanguageCode } from '@/components/LanguageSwitcher';

interface MenuCustomizationModalProps {
  isOpen: boolean;
  onClose: () => void;
  productId: string;
  productName: string;
  basePrice: number;
  menuDefinition: MenuDefinition;
  onAddToBasket: (selectedOptions: SelectedMenuOption[], totalPrice: number) => void;
  currentLanguage: LanguageCode;
}

const MenuCustomizationModal: React.FC<MenuCustomizationModalProps> = ({
  isOpen,
  onClose,
  productName,
  basePrice,
  menuDefinition,
  onAddToBasket,
  currentLanguage,
}) => {
  const { t } = useTranslation();
  const [selectedOptions, setSelectedOptions] = useState<Map<string, SelectedMenuOption[]>>(new Map());
  const [validationErrors, setValidationErrors] = useState<Map<string, string>>(new Map());
  const [itemCustomizations, setItemCustomizations] = useState<Map<string, ProductCustomization>>(new Map());
  const [customizingItem, setCustomizingItem] = useState<{ sectionId: string; itemId: string } | null>(null);

  // Helper function to get translated ingredient names
  const getIngredientNames = (item: any): string => {
    if (item.detailedIngredients && item.detailedIngredients.length > 0) {
      return item.detailedIngredients
        .map((ing: any) => ing.content?.[currentLanguage]?.name || ing.content?.en?.name || ing.name)
        .join(', ');
    }
    return item.ingredients?.join(', ') || '';
  };

  useEffect(() => {
    if (isOpen) {
      // Initialize with default selections
      const defaults = new Map<string, SelectedMenuOption[]>();
      menuDefinition.sections.forEach(section => {
        const defaultItems = section.items.filter(item => item.isDefault);

        // Respect maxSelection when initializing defaults
        const itemsToSelect = defaultItems.slice(0, section.maxSelection);

        if (itemsToSelect.length > 0) {
          defaults.set(
            section.id,
            itemsToSelect.map(item => ({
              sectionId: section.id,
              itemId: item.productId,
              quantity: 1,
            }))
          );
        }
      });
      setSelectedOptions(defaults);
      setValidationErrors(new Map());
    }
  }, [isOpen, menuDefinition]);

  const handleOptionToggle = (section: MenuSection, itemId: string) => {
    const sectionSelections = selectedOptions.get(section.id) || [];
    const existingIndex = sectionSelections.findIndex(opt => opt.itemId === itemId);

    let newSelections: SelectedMenuOption[];

    if (section.maxSelection === 1) {
      // Radio button behavior - replace selection
      newSelections = [{
        sectionId: section.id,
        itemId,
        quantity: 1,
      }];
    } else {
      // Checkbox behavior
      if (existingIndex >= 0) {
        // Remove selection
        newSelections = sectionSelections.filter((_, i) => i !== existingIndex);
      } else {
        // Add selection if not at max
        if (sectionSelections.length < section.maxSelection) {
          newSelections = [
            ...sectionSelections,
            { sectionId: section.id, itemId, quantity: 1 },
          ];
        } else {
          return; // Max reached, don't add
        }
      }
    }

    const newMap = new Map(selectedOptions);
    if (newSelections.length > 0) {
      newMap.set(section.id, newSelections);
    } else {
      newMap.delete(section.id);
    }
    setSelectedOptions(newMap);

    // Clear validation error for this section
    const newErrors = new Map(validationErrors);
    newErrors.delete(section.id);
    setValidationErrors(newErrors);
  };

  const isOptionSelected = (sectionId: string, itemId: string): boolean => {
    const sectionSelections = selectedOptions.get(sectionId) || [];
    return sectionSelections.some(opt => opt.itemId === itemId);
  };

  const calculateTotalPrice = (): number => {
    let total = basePrice;
    selectedOptions.forEach(sectionSelections => {
      sectionSelections.forEach(selection => {
        const section = menuDefinition.sections.find(s => s.id === selection.sectionId);
        const item = section?.items.find(i => i.productId === selection.itemId);
        if (item) {
          total += item.additionalPrice * selection.quantity;

          // Add customization price if exists
          const customizationKey = `${selection.sectionId}-${selection.itemId}`;
          const customization = itemCustomizations.get(customizationKey);
          if (customization) {
            total += customization.totalPrice - (item.additionalPrice || 0);
          }
        }
      });
    });
    return total;
  };

  const validateSelections = (): boolean => {
    const errors = new Map<string, string>();
    let isValid = true;

    menuDefinition.sections.forEach(section => {
      const sectionSelections = selectedOptions.get(section.id) || [];
      const totalQuantity = sectionSelections.reduce((sum, opt) => sum + opt.quantity, 0);

      if (section.isRequired && totalQuantity < section.minSelection) {
        errors.set(
          section.id,
          `${t('please_select_at_least')} ${section.minSelection} ${t('options')}`
        );
        isValid = false;
      }
    });

    setValidationErrors(errors);
    return isValid;
  };

  const handleAddToBasket = () => {
    if (!validateSelections()) {
      return;
    }

    const allSelections: SelectedMenuOption[] = [];
    selectedOptions.forEach(sectionSelections => {
      sectionSelections.forEach(selection => {
        const customizationKey = `${selection.sectionId}-${selection.itemId}`;
        const customization = itemCustomizations.get(customizationKey);

        // Add customization data to the selection
        const enrichedSelection: SelectedMenuOption = {
          ...selection,
          ...(customization && {
            selectedIngredients: customization.selectedIngredients,
            excludedIngredients: customization.excludedIngredients,
            ingredientQuantities: customization.ingredientQuantities,
            selectedSideItems: customization.selectedSideItems,
            specialInstructions: customization.specialInstructions,
          }),
        };

        allSelections.push(enrichedSelection);
      });
    });

    const totalPrice = calculateTotalPrice();
    onAddToBasket(allSelections, totalPrice);
    onClose();
  };

  const handleCustomizeItem = (sectionId: string, itemId: string) => {
    setCustomizingItem({ sectionId, itemId });
  };

  const handleCustomizationConfirm = (customization: ProductCustomization) => {
    if (!customizingItem) return;

    const customizationKey = `${customizingItem.sectionId}-${customizingItem.itemId}`;
    const newCustomizations = new Map(itemCustomizations);
    newCustomizations.set(customizationKey, customization);
    setItemCustomizations(newCustomizations);
    setCustomizingItem(null);
  };

  const getItemForCustomization = () => {
    if (!customizingItem) return null;

    const section = menuDefinition.sections.find(s => s.id === customizingItem.sectionId);
    const item = section?.items.find(i => i.productId === customizingItem.itemId);
    return item || null;
  };

  if (!isOpen) return null;

  const totalPrice = calculateTotalPrice();

  return (
    <div className={styles.modalOverlay} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2>{productName}</h2>
          <button onClick={onClose} className={styles.closeButton} aria-label={t('close')}>
            ×
          </button>
        </div>

        <div className={styles.modalBody}>
          <div className={styles.priceInfo}>
            <span>{t('base_price')}:</span>
            <span className={styles.price}>CHF {basePrice.toFixed(2)}</span>
          </div>

          {menuDefinition.sections.map(section => {
            const sectionSelections = selectedOptions.get(section.id) || [];
            const totalQuantity = sectionSelections.reduce((sum, opt) => sum + opt.quantity, 0);
            const hasError = validationErrors.has(section.id);

            return (
              <div key={section.id} className={styles.section}>
                <div className={styles.sectionHeader}>
                  <h3>
                    {section.name}
                    {section.isRequired && <span className={styles.required}>*</span>}
                  </h3>
                  {section.description && (
                    <p className={styles.sectionDescription}>{section.description}</p>
                  )}
                  <p className={styles.selectionInfo}>
                    {section.minSelection === section.maxSelection
                      ? `${t('choose')} ${section.maxSelection}`
                      : `${t('choose')} ${section.minSelection}-${section.maxSelection}`}
                    {totalQuantity > 0 && ` (${totalQuantity} ${t('selected')})`}
                  </p>
                </div>

                {hasError && (
                  <div className={styles.errorMessage}>{validationErrors.get(section.id)}</div>
                )}

                <div className={styles.optionsList}>
                  {section.items.map(item => {
                    const isSelected = isOptionSelected(section.id, item.productId);
                    const isDisabled =
                      !isSelected &&
                      section.maxSelection > 1 &&
                      totalQuantity >= section.maxSelection;

                    return (
                      <label
                        key={item.id}
                        className={`${styles.optionItem} ${isSelected ? styles.selected : ''} ${
                          isDisabled ? styles.disabled : ''
                        }`}
                      >
                        <input
                          type={section.maxSelection === 1 ? 'radio' : 'checkbox'}
                          name={`section-${section.id}`}
                          checked={isSelected}
                          onChange={() => handleOptionToggle(section, item.productId)}
                          disabled={isDisabled}
                          className={styles.optionInput}
                        />
                        <div className={styles.optionDetails}>
                          <div className={styles.optionHeader}>
                            <span className={styles.optionName}>{item.productName}</span>
                            {item.additionalPrice > 0 && (
                              <span className={styles.optionPrice}>
                                +${item.additionalPrice.toFixed(2)}
                              </span>
                            )}
                          </div>

                          {getIngredientNames(item) && (
                            <div className={styles.optionIngredients}>
                              {getIngredientNames(item)}
                            </div>
                          )}

                          {item.allergens && item.allergens.length > 0 && (
                            <AllergenDisplay
                              allergens={item.allergens}
                              variant="compact"
                              maxVisible={5}
                              showLabel={false}
                              className={styles.optionAllergens}
                            />
                          )}

                          {/* Customize button for selected items with customizable options */}
                          {isSelected && (item.detailedIngredients?.length || item.suggestedSideItems?.length) && (
                            <button
                              onClick={(e) => {
                                e.preventDefault();
                                handleCustomizeItem(section.id, item.productId);
                              }}
                              className={styles.customizeButton}
                            >
                              {t('customize')}
                            </button>
                          )}
                        </div>
                      </label>
                    );
                  })}
                </div>
              </div>
            );
          })}
        </div>

        <div className={styles.modalFooter}>
          <div className={styles.totalPrice}>
            <span>{t('total')}:</span>
            <span className={styles.price}>CHF {totalPrice.toFixed(2)}</span>
          </div>
          <button onClick={handleAddToBasket} className={styles.addButton}>
            {t('add_to_basket')}
          </button>
        </div>
      </div>

      {/* Product Customization Modal */}
      {customizingItem && getItemForCustomization() && (
        <ProductCustomizationInBundle
          isOpen={!!customizingItem}
          onClose={() => setCustomizingItem(null)}
          productName={getItemForCustomization()?.productName || ''}
          basePrice={getItemForCustomization()?.additionalPrice || 0}
          detailedIngredients={getItemForCustomization()?.detailedIngredients}
          suggestedSideItems={getItemForCustomization()?.suggestedSideItems}
          onConfirm={handleCustomizationConfirm}
          initialCustomization={itemCustomizations.get(`${customizingItem.sectionId}-${customizingItem.itemId}`)}
          currentLanguage={currentLanguage}
        />
      )}
    </div>
  );
};

export default MenuCustomizationModal;
