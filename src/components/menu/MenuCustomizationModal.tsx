'use client';

import React, { useState, useEffect } from 'react';
import styles from './MenuCustomization.module.css';
import { MenuDefinition, MenuSection, SelectedMenuOption } from '@/types/menu';

interface MenuCustomizationModalProps {
  isOpen: boolean;
  onClose: () => void;
  productId: string;
  productName: string;
  basePrice: number;
  menuDefinition: MenuDefinition;
  onAddToBasket: (selectedOptions: SelectedMenuOption[], totalPrice: number) => void;
}

const MenuCustomizationModal: React.FC<MenuCustomizationModalProps> = ({
  isOpen,
  onClose,
  productName,
  basePrice,
  menuDefinition,
  onAddToBasket,
}) => {
  const [selectedOptions, setSelectedOptions] = useState<Map<string, SelectedMenuOption[]>>(new Map());
  const [validationErrors, setValidationErrors] = useState<Map<string, string>>(new Map());

  useEffect(() => {
    if (isOpen) {
      // Initialize with default selections
      const defaults = new Map<string, SelectedMenuOption[]>();
      menuDefinition.sections.forEach(section => {
        const defaultItems = section.items.filter(item => item.isDefault);
        if (defaultItems.length > 0) {
          defaults.set(
            section.id,
            defaultItems.map(item => ({
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
          `Please select at least ${section.minSelection} option(s)`
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
      allSelections.push(...sectionSelections);
    });

    const totalPrice = calculateTotalPrice();
    onAddToBasket(allSelections, totalPrice);
    onClose();
  };

  if (!isOpen) return null;

  const totalPrice = calculateTotalPrice();

  return (
    <div className={styles.modalOverlay} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2>{productName}</h2>
          <button onClick={onClose} className={styles.closeButton}>
            ×
          </button>
        </div>

        <div className={styles.modalBody}>
          <div className={styles.priceDisplay}>
            <span>Base Price:</span>
            <span className={styles.price}>${basePrice.toFixed(2)}</span>
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
                      ? `Choose ${section.maxSelection}`
                      : `Choose ${section.minSelection}-${section.maxSelection}`}
                    {totalQuantity > 0 && ` (${totalQuantity} selected)`}
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
                          <span className={styles.optionName}>{item.productName}</span>
                          {item.additionalPrice > 0 && (
                            <span className={styles.optionPrice}>
                              +${item.additionalPrice.toFixed(2)}
                            </span>
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
            <span>Total:</span>
            <span className={styles.price}>${totalPrice.toFixed(2)}</span>
          </div>
          <button onClick={handleAddToBasket} className={styles.addButton}>
            Add to Basket
          </button>
        </div>
      </div>
    </div>
  );
};

export default MenuCustomizationModal;
