'use client';

import React, { useState, useEffect } from 'react';
import styles from './MenuEditor.module.css';
import { MenuSectionItem } from '@/types/menu';
import { searchProducts } from '@/services/productService';

interface MenuItemSelectorProps {
  items: MenuSectionItem[];
  onChange: (items: MenuSectionItem[]) => void;
}

interface Product {
  id: string;
  name: string;
  basePrice: number;
}

const MenuItemSelector: React.FC<MenuItemSelectorProps> = ({ items, onChange }) => {
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<Product[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [showResults, setShowResults] = useState(false);

  // Debounced search
  useEffect(() => {
    if (searchQuery.length < 2) {
      setSearchResults([]);
      setShowResults(false);
      return;
    }

    const timer = setTimeout(async () => {
      setIsSearching(true);
      try {
        const response: any = await searchProducts(searchQuery);
        if (response.success && response.data) {
          setSearchResults(response.data.items || response.data || []);
          setShowResults(true);
        }
      } catch (error) {
        console.error('Error searching products:', error);
        setSearchResults([]);
      } finally {
        setIsSearching(false);
      }
    }, 300);

    return () => clearTimeout(timer);
  }, [searchQuery]);

  const addItem = (product: Product) => {
    const newItem: MenuSectionItem = {
      id: `temp-${Date.now()}`,
      productId: product.id,
      productName: product.name,
      additionalPrice: 0,
      displayOrder: items.length,
      isDefault: items.length === 0, // First item is default
    };
    onChange([...items, newItem]);
    setSearchQuery('');
    setShowResults(false);
  };

  const updateItem = (index: number, updates: Partial<MenuSectionItem>) => {
    const newItems = [...items];
    newItems[index] = { ...newItems[index], ...updates };
    onChange(newItems);
  };

  const removeItem = (index: number) => {
    const newItems = items.filter((_, i) => i !== index);
    onChange(newItems);
  };

  const moveItem = (index: number, direction: 'up' | 'down') => {
    if (
      (direction === 'up' && index === 0) ||
      (direction === 'down' && index === items.length - 1)
    ) {
      return;
    }

    const newItems = [...items];
    const targetIndex = direction === 'up' ? index - 1 : index + 1;
    [newItems[index], newItems[targetIndex]] = [
      newItems[targetIndex],
      newItems[index],
    ];

    // Update display orders
    newItems.forEach((item, i) => {
      item.displayOrder = i;
    });

    onChange(newItems);
  };

  return (
    <div className={styles.itemSelector}>
      <h4 className={styles.sectionTitle}>Section Items</h4>

      {/* Product Search */}
      <div className={styles.productSearch}>
        <input
          type="text"
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          placeholder="Search products to add..."
          className={styles.searchInput}
        />
        {showResults && searchResults.length > 0 && (
          <div className={styles.searchResults}>
            {searchResults.map((product) => (
              <div
                key={product.id}
                onClick={() => addItem(product)}
                className={styles.searchResultItem}
              >
                <div>{product.name}</div>
                <div className={styles.helpText}>${product.basePrice.toFixed(2)}</div>
              </div>
            ))}
          </div>
        )}
        {isSearching && (
          <div className={styles.searchResults}>
            <div className={styles.searchResultItem}>Searching...</div>
          </div>
        )}
      </div>

      {/* Items Table */}
      {items.length > 0 ? (
        <table className={styles.itemsTable}>
          <thead>
            <tr>
              <th>Order</th>
              <th>Product</th>
              <th>Additional Price</th>
              <th>Default</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item, index) => (
              <tr key={item.id}>
                <td>
                  <div style={{ display: 'flex', gap: '0.25rem' }}>
                    <button
                      onClick={() => moveItem(index, 'up')}
                      disabled={index === 0}
                      className={styles.iconButton}
                      title="Move up"
                    >
                      ↑
                    </button>
                    <button
                      onClick={() => moveItem(index, 'down')}
                      disabled={index === items.length - 1}
                      className={styles.iconButton}
                      title="Move down"
                    >
                      ↓
                    </button>
                  </div>
                </td>
                <td>{item.productName}</td>
                <td>
                  <input
                    type="number"
                    step="0.01"
                    min="0"
                    value={item.additionalPrice}
                    onChange={(e) =>
                      updateItem(index, {
                        additionalPrice: parseFloat(e.target.value) || 0,
                      })
                    }
                    className={styles.input}
                    style={{ width: '100px' }}
                  />
                </td>
                <td>
                  <input
                    type="checkbox"
                    checked={item.isDefault}
                    onChange={(e) =>
                      updateItem(index, { isDefault: e.target.checked })
                    }
                    className={styles.checkbox}
                  />
                </td>
                <td>
                  <button
                    onClick={() => removeItem(index)}
                    className={`${styles.iconButton} ${styles.danger}`}
                    title="Remove item"
                  >
                    ×
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <div className={styles.emptyState}>
          <p>No items added yet. Search and add products above.</p>
        </div>
      )}
    </div>
  );
};

export default MenuItemSelector;
