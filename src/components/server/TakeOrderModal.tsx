import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getCategories,
  createServerOrder,
  Product,
  Category,
  UserDto,
  calculateDiscountFromPoints,
} from '@/services/serverService';
import { getProducts } from '@/services/menuService';
import { CreateOrderItemDto } from '@/types/order';
import ProductCustomization, { CustomizationResult } from './ProductCustomization';
import CustomerSearchInput from './CustomerSearchInput';
import CustomerInfoPanel from './CustomerInfoPanel';
import styles from './TakeOrderModal.module.css';

interface TakeOrderModalProps {
  tableNumber: string;
  onClose: () => void;
  onOrderCreated: () => void;
}

interface OrderItem {
  product: Product;
  quantity: number;
  variationId?: string;
  variationName?: string;
  notes?: string;
  excludedIngredients?: string[];
  addedIngredients?: Array<{ id: string; name: string; price: number }>;
  sideItems?: Array<{ id: string; name: string; quantity: number; price: number }>;
  unitPrice: number;
}

export default function TakeOrderModal({
  tableNumber,
  onClose,
  onOrderCreated,
}: TakeOrderModalProps) {
  const { t } = useTranslation();
  const [products, setProducts] = useState<Product[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selectedCategory, setSelectedCategory] = useState<string | null>(null);
  const [orderItems, setOrderItems] = useState<OrderItem[]>([]);
  const [customerName, setCustomerName] = useState('');
  const [orderNotes, setOrderNotes] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');

  // Customization modal state
  const [selectedProductForCustomization, setSelectedProductForCustomization] = useState<Product | null>(null);

  // Customer loyalty state
  const [selectedUser, setSelectedUser] = useState<UserDto | null>(null);
  const [pointsToRedeem, setPointsToRedeem] = useState(0);

  // Load categories once on mount
  useEffect(() => {
    async function loadCategories() {
      try {
        const categoriesData = await getCategories();
        const activeCategories = categoriesData.filter(c => c.isActive);
        setCategories(activeCategories);
      } catch (err) {
        console.error('Failed to load categories:', err);
      }
    }
    loadCategories();
  }, []);

  // Fetch products when category changes
  useEffect(() => {
    async function fetchProducts() {
      try {
        setIsLoading(true);
        setError(null);
        // Use menuService getProducts with optional categoryId for server-side filtering
        const response = await getProducts(1, 100, selectedCategory || undefined);
        if (response.success && response.data?.items) {
          // Map to Product type
          const mappedProducts: Product[] = response.data.items.map((p: any) => ({
            id: p.id,
            name: p.name,
            description: p.description,
            basePrice: p.basePrice,
            isActive: p.isActive,
            isAvailable: p.isAvailable,
            type: p.type,
            categories: p.categories,
            primaryCategoryId: p.primaryCategoryId,
            imageUrl: p.imageUrl,
            variations: p.variations,
          }));
          setProducts(mappedProducts.filter(p => p.isActive && p.isAvailable));
        } else {
          setProducts([]);
        }
      } catch (err) {
        console.error('Failed to load products:', err);
        setError('Failed to load menu items');
        setProducts([]);
      } finally {
        setIsLoading(false);
      }
    }
    fetchProducts();
  }, [selectedCategory]);

  // Filter products by search (category filtering is done server-side)
  const filteredProducts = useMemo(() => {
    if (!searchQuery) return products;

    const query = searchQuery.toLowerCase();
    return products.filter(p =>
      p.name.toLowerCase().includes(query) ||
      p.description?.toLowerCase().includes(query)
    );
  }, [products, searchQuery]);

  // Calculate subtotal
  const orderSubtotal = useMemo(() => {
    return orderItems.reduce((sum, item) => {
      return sum + item.unitPrice * item.quantity;
    }, 0);
  }, [orderItems]);

  // Calculate discount from points
  const pointsDiscount = useMemo(() => {
    return calculateDiscountFromPoints(pointsToRedeem);
  }, [pointsToRedeem]);

  // Calculate total with discount
  const orderTotal = useMemo(() => {
    return Math.max(0, orderSubtotal - pointsDiscount);
  }, [orderSubtotal, pointsDiscount]);

  // Handle product click - open customization modal
  const handleProductClick = useCallback((product: Product) => {
    setSelectedProductForCustomization(product);
  }, []);

  // Handle customization confirm
  const handleCustomizationConfirm = useCallback((result: CustomizationResult) => {
    const product = selectedProductForCustomization;
    if (!product) return;

    setOrderItems(prev => {
      // Check if identical item already exists
      const existingIndex = prev.findIndex(item =>
        item.product.id === product.id &&
        item.variationId === result.variationId &&
        JSON.stringify(item.excludedIngredients) === JSON.stringify(result.excludedIngredients) &&
        JSON.stringify(item.addedIngredients?.map(i => i.id)) === JSON.stringify(result.addedIngredients.map(i => i.id)) &&
        JSON.stringify(item.sideItems?.map(s => s.id)) === JSON.stringify(result.sideItems.map(s => s.id))
      );

      if (existingIndex >= 0) {
        // Increment quantity
        const updated = [...prev];
        updated[existingIndex].quantity += 1;
        return updated;
      }

      // Build customization notes
      const noteParts: string[] = [];
      if (result.variationName) {
        noteParts.push(result.variationName);
      }
      if (result.excludedIngredients.length > 0) {
        noteParts.push(`No: ${result.excludedIngredients.join(', ')}`);
      }
      if (result.addedIngredients.length > 0) {
        noteParts.push(`Add: ${result.addedIngredients.map(i => i.name).join(', ')}`);
      }
      if (result.sideItems.length > 0) {
        noteParts.push(`Sides: ${result.sideItems.map(s => s.name).join(', ')}`);
      }
      if (result.specialInstructions) {
        noteParts.push(result.specialInstructions);
      }

      return [...prev, {
        product,
        quantity: 1,
        variationId: result.variationId,
        variationName: result.variationName,
        notes: noteParts.join(' | ') || undefined,
        excludedIngredients: result.excludedIngredients,
        addedIngredients: result.addedIngredients,
        sideItems: result.sideItems,
        unitPrice: result.finalPrice,
      }];
    });

    setSelectedProductForCustomization(null);
  }, [selectedProductForCustomization]);

  // Update item quantity
  const updateQuantity = (index: number, quantity: number) => {
    if (quantity <= 0) {
      setOrderItems(prev => prev.filter((_, i) => i !== index));
    } else {
      setOrderItems(prev => {
        const updated = [...prev];
        updated[index].quantity = quantity;
        return updated;
      });
    }
  };

  // Remove item
  const removeItem = (index: number) => {
    setOrderItems(prev => prev.filter((_, i) => i !== index));
  };

  // Submit order
  const handleSubmit = async () => {
    if (orderItems.length === 0) {
      setError('Please add at least one item to the order');
      return;
    }

    try {
      setIsSubmitting(true);
      setError(null);

      const items: CreateOrderItemDto[] = orderItems.map(item => ({
        productId: item.product.id,
        productVariationId: item.variationId,
        quantity: item.quantity,
        unitPrice: item.unitPrice,
        specialInstructions: item.notes,
        // Note: excludedIngredients, addedIngredients, sideItems would need backend support
      }));

      await createServerOrder(
        parseInt(tableNumber, 10),
        items,
        customerName || undefined,
        orderNotes || undefined,
        selectedUser?.id,
        pointsToRedeem > 0 ? pointsToRedeem : undefined
      );

      onOrderCreated();
      onClose();
    } catch (err) {
      console.error('Failed to create order:', err);
      setError(err instanceof Error ? err.message : 'Failed to create order');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <>
      <div className={styles.overlay} onClick={onClose}>
        <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
          <div className={styles.header}>
            <div className={styles.headerInfo}>
              <h2>{t('server.take_order', 'Take Order')}</h2>
              <span className={styles.tableLabel}>
                {t('server.table', 'Table')} {tableNumber}
              </span>
            </div>
            <button className={styles.closeButton} onClick={onClose}>✕</button>
          </div>

          <div className={styles.content}>
            {/* Left Panel - Menu */}
            <div className={styles.menuPanel}>
              <div className={styles.searchBar}>
                <input
                  type="text"
                  placeholder={t('server.search_items', 'Search items...')}
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className={styles.searchInput}
                />
              </div>

              <div className={styles.categories}>
                <button
                  className={`${styles.categoryButton} ${!selectedCategory ? styles.categoryActive : ''}`}
                  onClick={() => setSelectedCategory(null)}
                >
                  {t('server.all', 'All')}
                </button>
                {categories.map(category => (
                  <button
                    key={category.id}
                    className={`${styles.categoryButton} ${
                      selectedCategory === category.id ? styles.categoryActive : ''
                    }`}
                    onClick={() => setSelectedCategory(category.id)}
                  >
                    {category.name}
                  </button>
                ))}
              </div>

              {isLoading ? (
                <div className={styles.loading}>
                  <div className={styles.spinner}></div>
                  <span>{t('server.loading_menu', 'Loading menu...')}</span>
                </div>
              ) : (
                <div className={styles.productGrid}>
                  {filteredProducts.map(product => (
                    <button
                      key={product.id}
                      className={styles.productCard}
                      onClick={() => handleProductClick(product)}
                    >
                      <span className={styles.productName}>{product.name}</span>
                      <span className={styles.productPrice}>
                        CHF {product.basePrice.toFixed(2)}
                      </span>
                    </button>
                  ))}
                  {filteredProducts.length === 0 && !isLoading && (
                    <div className={styles.noProducts}>
                      {t('server.no_products', 'No products found')}
                    </div>
                  )}
                </div>
              )}
            </div>

            {/* Right Panel - Order Summary */}
            <div className={styles.orderPanel}>
              <h3 className={styles.orderTitle}>
                {t('server.order_summary', 'Order Summary')}
              </h3>

              {error && (
                <div className={styles.error}>{error}</div>
              )}

              <div className={styles.customerInfo}>
                <CustomerSearchInput
                  value={customerName}
                  selectedUser={selectedUser}
                  onValueChange={setCustomerName}
                  onUserSelect={(user) => {
                    setSelectedUser(user);
                    setPointsToRedeem(0); // Reset points when user changes
                  }}
                />
              </div>

              {selectedUser && (
                <CustomerInfoPanel
                  user={selectedUser}
                  orderTotal={orderSubtotal}
                  pointsToRedeem={pointsToRedeem}
                  onPointsChange={setPointsToRedeem}
                />
              )}

              <div className={styles.orderItems}>
                {orderItems.length === 0 ? (
                  <div className={styles.emptyOrder}>
                    {t('server.no_items', 'No items added yet')}
                  </div>
                ) : (
                  orderItems.map((item, index) => (
                    <div key={index} className={styles.orderItem}>
                      <div className={styles.orderItemInfo}>
                        <span className={styles.orderItemName}>
                          {item.product.name}
                          {item.variationName && (
                            <span className={styles.variationLabel}> ({item.variationName})</span>
                          )}
                        </span>
                        {item.notes && (
                          <span className={styles.orderItemNotes}>{item.notes}</span>
                        )}
                        <span className={styles.orderItemPrice}>
                          CHF {(item.unitPrice * item.quantity).toFixed(2)}
                        </span>
                      </div>
                      <div className={styles.orderItemActions}>
                        <button
                          className={styles.qtyButton}
                          onClick={() => updateQuantity(index, item.quantity - 1)}
                        >
                          −
                        </button>
                        <span className={styles.qtyValue}>{item.quantity}</span>
                        <button
                          className={styles.qtyButton}
                          onClick={() => updateQuantity(index, item.quantity + 1)}
                        >
                          +
                        </button>
                        <button
                          className={styles.removeButton}
                          onClick={() => removeItem(index)}
                        >
                          🗑️
                        </button>
                      </div>
                    </div>
                  ))
                )}
              </div>

              <div className={styles.orderNotes}>
                <textarea
                  placeholder={t('server.order_notes', 'Order notes (optional)')}
                  value={orderNotes}
                  onChange={(e) => setOrderNotes(e.target.value)}
                  className={styles.notesInput}
                  rows={2}
                />
              </div>

              <div className={styles.orderFooter}>
                <div className={styles.orderTotal}>
                  <span>{t('server.total', 'Total')}</span>
                  <span className={styles.totalAmount}>
                    CHF {orderTotal.toFixed(2)}
                  </span>
                </div>

                <button
                  className={styles.submitButton}
                  onClick={handleSubmit}
                  disabled={isSubmitting || orderItems.length === 0}
                >
                  {isSubmitting
                    ? t('server.creating_order', 'Creating Order...')
                    : t('server.place_order', 'Place Order')}
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Product Customization Modal */}
      {selectedProductForCustomization && (
        <ProductCustomization
          product={selectedProductForCustomization}
          isOpen={!!selectedProductForCustomization}
          onClose={() => setSelectedProductForCustomization(null)}
          onConfirm={handleCustomizationConfirm}
        />
      )}
    </>
  );
}
