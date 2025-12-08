import React, { useState } from 'react';
import { Controller } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { SuggestedSideItemsPickerProps, ProductSearchResult } from './types';
import { getProducts } from '@/services/menuService';
import styles from '@/app/styles/AdminPage.module.css';
import modalStyles from '@/app/styles/RegisterStaffModal.module.css';
import detailsStyles from '@/app/styles/DetailsPage.module.css';

export const SuggestedSideItemsPicker: React.FC<SuggestedSideItemsPickerProps> = ({
  errors,
  control,
  selectedSideItemIds,
  onChange,
}) => {
  const { t } = useTranslation();
  const [showPicker, setShowPicker] = useState(false);
  const [search, setSearch] = useState('');
  const [results, setResults] = useState<ProductSearchResult[]>([]);
  const [tempSelectedIds, setTempSelectedIds] = useState<string[]>([]);
  const [selectedItemsDetails, setSelectedItemsDetails] = useState<Map<string, { name: string; description?: string }>>(new Map());

  // Fetch details for selected side items on mount or when selectedSideItemIds change
  React.useEffect(() => {
    const fetchSelectedItemsDetails = async () => {
      if (selectedSideItemIds.length === 0) {
        setSelectedItemsDetails(new Map());
        return;
      }

      try {
        const resp = await getProducts(1, 100, undefined);
        if (resp.success) {
          const detailsMap = new Map<string, { name: string; description?: string }>();
          selectedSideItemIds.forEach((id: string) => {
            const item = resp.data.items.find((p: any) => p.id === id);
            if (item) {
              detailsMap.set(id, { name: item.name, description: item.description });
            }
          });
          setSelectedItemsDetails(detailsMap);
        }
      } catch {
        // Handle error silently
      }
    };

    fetchSelectedItemsDetails();
  }, [selectedSideItemIds]);

  const runSearch = async () => {
    if (!search.trim()) return;

    try {
      const resp = await getProducts(1, 20, undefined);
      if (resp.success) {
        const filteredItems = resp.data.items
          .filter((p: any) =>
            p.name.toLowerCase().includes(search.toLowerCase()) // Allow any product type as side item
          )
          .map((p: any) => ({
            id: p.id,
            name: p.name,
            description: p.description,
            basePrice: p.basePrice,
            type: p.type,
          }));
        setResults(filteredItems);
      }
    } catch {
      // Handle search error silently
      setResults([]);
    }
  };

  const toggleSelect = (id: string, checked: boolean) => {
    setTempSelectedIds(prev =>
      checked
        ? Array.from(new Set([...prev, id]))
        : prev.filter(x => x !== id)
    );
  };

  const saveSelected = () => {
    const newSelectedIds = Array.from(new Set([...selectedSideItemIds, ...tempSelectedIds]));
    onChange(newSelectedIds);
    setShowPicker(false);
    setTempSelectedIds([]);
    setSearch('');
    setResults([]);
  };

  const removeItem = (idToRemove: string) => {
    const updatedIds = selectedSideItemIds.filter(id => id !== idToRemove);
    onChange(updatedIds);
  };

  const getSelectedItemsDisplay = () => {
    if (selectedSideItemIds.length === 0) {
      return <p className={modalStyles.emptyState}>{t('no_side_items_selected')}</p>;
    }

    return (
      <div className={modalStyles.chipGroup}>
        {selectedSideItemIds.map(id => {
          // Get the item name from fetched details, fallback to results, or show ID
          const itemDetails = selectedItemsDetails.get(id);
          const resultItem = results.find(r => r.id === id);
          const displayName = itemDetails?.name || resultItem?.name || `Item ${id.substring(0, 8)}...`;

          return (
            <div key={id} className={modalStyles.chip}>
              <span>{displayName}</span>
              <button
                type="button"
                onClick={() => removeItem(id)}
                className={modalStyles.chipRemove}
                aria-label={t('remove')}
              >
                ×
              </button>
            </div>
          );
        })}
      </div>
    );
  };

  return (
    <div className={modalStyles.formGroup}>
      <h3>{t('suggested_side_items')} {t('optional')}</h3>
      {errors.suggestedSideItemIds && (
        <p className={modalStyles.errorMessage}>{errors.suggestedSideItemIds.message}</p>
      )}

      {getSelectedItemsDisplay()}

      <button
        type="button"
        className={`${styles.adminButton} ${styles.add}`}
        onClick={() => setShowPicker(true)}
      >
        {t('add_side_items')}
      </button>

      {showPicker && (
        <div className={detailsStyles.formGrid}>
          <div className={modalStyles.formGroup}>
            <label>{t('search_side_items')}</label>
            <input
              type="text"
              placeholder={t('search_placeholder')}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onKeyPress={(e) => {
                if (e.key === 'Enter' && search.trim()) {
                  runSearch();
                }
              }}
            />
          </div>

          {results.length > 0 && (
            <div className={modalStyles.chipGroup}>
              {results.map(product => {
                const isSelected = tempSelectedIds.includes(product.id);
                const isAlreadyAdded = selectedSideItemIds.includes(product.id);
                const chipId = `side-item-${product.id}`;

                return (
                  <div
                    key={product.id}
                    className={`${modalStyles.chip} ${isAlreadyAdded ? modalStyles.chipDisabled : ''}`}
                  >
                    <input
                      type="checkbox"
                      id={chipId}
                      checked={isSelected}
                      disabled={isAlreadyAdded}
                      onChange={(e) => toggleSelect(product.id, e.target.checked)}
                    />
                    <label htmlFor={chipId}>
                      {product.name}
                      {isAlreadyAdded && <span className={modalStyles.chipNote}> ({t('already_added')})</span>}
                    </label>
                  </div>
                );
              })}
            </div>
          )}

          {search && results.length === 0 && (
            <p className={modalStyles.emptyState}>{t('no_side_items_found')}</p>
          )}

          <div className={detailsStyles.actionRow}>
            <button
              type="button"
              className={`${styles.adminButton}`}
              onClick={runSearch}
              disabled={!search.trim()}
            >
              {t('search')}
            </button>
            <button
              type="button"
              className={styles.cancelButton}
              onClick={() => {
                setShowPicker(false);
                setTempSelectedIds([]);
                setSearch('');
                setResults([]);
              }}
            >
              {t('cancel')}
            </button>
            <button
              type="button"
              className={`${styles.adminButton} ${styles.save}`}
              onClick={saveSelected}
              disabled={tempSelectedIds.length === 0}
            >
              {t('add_selected')}
            </button>
          </div>
        </div>
      )}

      {/* Hidden input for form registration */}
      <Controller
        name="suggestedSideItemIds"
        control={control}
        render={({ field }) => (
          <input
            type="hidden"
            {...field}
            value={selectedSideItemIds.join(',')}
          />
        )}
      />
    </div>
  );
};
