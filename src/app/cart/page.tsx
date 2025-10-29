"use client";

import React, { useState } from 'react';
import Link from 'next/link';
import Image from 'next/image';
import { useCart } from '@/components/cart/CartContext';
import styles from '../styles/CartPage.module.css';
import { useTranslation } from 'react-i18next';
import { Trash2, Plus, Minus, ShoppingCart, Tag, Loader2 } from 'lucide-react';

export default function CartPage() {
  const { state, removeItem, updateItem, applyPromoCode, removePromoCode, getTotal, getItemCount } = useCart();
  const { t } = useTranslation();
  const [promoCode, setPromoCode] = useState('');
  const [isApplyingPromo, setIsApplyingPromo] = useState(false);
  const [editingInstructions, setEditingInstructions] = useState<string | null>(null);
  const [instructionsValue, setInstructionsValue] = useState('');

  const handleRemoveItem = async (basketItemId: string | undefined) => {
    if (!basketItemId) return;
    try {
      await removeItem(basketItemId);
    } catch {
      // Error already handled by CartContext
    }
  };

  const handleUpdateQuantity = async (basketItemId: string | undefined, newQuantity: number) => {
    if (!basketItemId || newQuantity < 1) return;
    try {
      await updateItem(basketItemId, newQuantity);
    } catch {
      // Error already handled by CartContext
    }
  };

  const handleApplyPromoCode = async () => {
    if (!promoCode.trim()) return;
    setIsApplyingPromo(true);
    try {
      await applyPromoCode(promoCode.trim());
      setPromoCode('');
    } finally {
      setIsApplyingPromo(false);
    }
  };

  const handleRemovePromoCode = async () => {
    try {
      await removePromoCode();
    } catch {
      // Error already handled by CartContext
    }
  };

  const handleSaveInstructions = async (basketItemId: string | undefined, quantity: number, instructions: string) => {
    if (!basketItemId) return;
    try {
      await updateItem(basketItemId, quantity, instructions);
      setEditingInstructions(null);
      setInstructionsValue('');
    } catch {
      // Error already handled by CartContext
    }
  };

  if (state.items.length === 0) {
    return (
      <main className={styles.cartContainer} aria-labelledby="cart-heading">
        <h1 id="cart-heading" className={styles.pageTitle}>{t('cart_title', 'Your Cart')}</h1>
        <div className={styles.emptyCartContainer}>
          <ShoppingCart className={styles.emptyCartIcon} size={64} />
          <p className={styles.emptyCartMessage}>{t('cart_empty_message', 'Your cart is empty')}</p>
          <Link href="/menu" className={styles.emptyCartLink}>
            {t('cart_browse_menu_button', 'Browse Menu')}
          </Link>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.cartContainer} aria-labelledby="cart-heading">
      <h1 id="cart-heading" className={styles.pageTitle}>{t('cart_title', 'Your Cart')}</h1>

      {/* Loading State */}
      {state.isLoading && (
        <div className={styles.loadingContainer}>
          <Loader2 className={styles.spinner} size={32} />
          <p>{t('loading', 'Loading...')}</p>
        </div>
      )}

      {/* Error State */}
      {state.error && (
        <div className={styles.errorContainer}>
          <p className={styles.errorMessage}>{state.error}</p>
        </div>
      )}

      <div className={styles.cartContent}>
        {/* Cart Items */}
        <div className={styles.cartItemsList}>
          {state.items.map((item) => {
            const itemId = item.basketItemId || item.id || item.productId;
            return (
              <div key={itemId} className={styles.cartItem}>
                {/* Item Image */}
                {item.productImageUrl && (
                  <div className={styles.itemImageContainer}>
                    <Image
                      src={item.productImageUrl}
                      alt={item.productName || 'Product'}
                      width={120}
                      height={120}
                      className={styles.itemImage}
                    />
                  </div>
                )}

                {/* Item Details */}
                <div className={styles.itemDetails}>
                  <h2 className={styles.itemName}>{item.productName || 'Unknown Item'}</h2>
                  {item.productDescription && (
                    <p className={styles.itemDescription}>{item.productDescription}</p>
                  )}
                  {item.variationName && (
                    <p className={styles.itemVariation}>{item.variationName}</p>
                  )}

                  {/* Price Breakdown */}
                  <div className={styles.priceBreakdownItem}>
                    <p className={styles.itemPrice}>
                      {t('base_price', 'Base Price')}: CHF {item.unitPrice.toFixed(2)}
                    </p>
                    {item.customizationPrice != null && item.customizationPrice > 0 && (
                      <p className={styles.customizationPrice}>
                        {t('customization_cost', 'Customizations')}: +CHF {item.customizationPrice.toFixed(2)}
                      </p>
                    )}
                  </div>

                  {/* Customizations */}
                  {(item.selectedIngredientNames?.length || item.excludedIngredientNames?.length || item.selectedSideItems?.length || item.specialInstructions) && (
                    <div className={styles.customizationsContainer}>
                      <h4 className={styles.customizationsTitle}>{t('customizations', 'Customizations')}:</h4>

                      {item.selectedIngredientNames && item.selectedIngredientNames.length > 0 && (
                        <div className={styles.customizationDetail}>
                          <span className={styles.customizationLabel}>{t('added_ingredients', 'Added')}:</span>
                          <span className={styles.customizationValue}>{item.selectedIngredientNames.join(', ')}</span>
                        </div>
                      )}

                      {item.excludedIngredientNames && item.excludedIngredientNames.length > 0 && (
                        <div className={styles.customizationDetail}>
                          <span className={styles.customizationLabel}>{t('removed_ingredients', 'Removed')}:</span>
                          <span className={styles.customizationValue}>{item.excludedIngredientNames.join(', ')}</span>
                        </div>
                      )}

                      {item.selectedSideItems && item.selectedSideItems.length > 0 && (
                        <div className={styles.customizationDetail}>
                          <span className={styles.customizationLabel}>{t('side_items', 'Side Items')}:</span>
                          <span className={styles.customizationValue}>
                            {item.selectedSideItems.map((sideItem, idx) => (
                              <span key={sideItem.id}>
                                {sideItem.name} x{sideItem.quantity} (CHF {sideItem.subTotal.toFixed(2)})
                                {idx < item.selectedSideItems!.length - 1 ? ', ' : ''}
                              </span>
                            ))}
                          </span>
                        </div>
                      )}

                      {item.specialInstructions && (
                        <div className={styles.customizationDetail}>
                          <span className={styles.customizationLabel}>{t('special_requests', 'Special Requests')}:</span>
                          <span className={styles.customizationValue}>{item.specialInstructions}</span>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Special Instructions Edit Section */}
                  {!item.selectedIngredientNames?.length && !item.excludedIngredientNames?.length && (
                    <div className={styles.instructionsContainer}>
                      {editingInstructions === itemId ? (
                        <div className={styles.instructionsEdit}>
                        <textarea
                          value={instructionsValue}
                          onChange={(e) => setInstructionsValue(e.target.value)}
                          placeholder={t('special_instructions_placeholder', 'Add special instructions...')}
                          className={styles.instructionsTextarea}
                          rows={3}
                        />
                        <div className={styles.instructionsActions}>
                          <button
                            type="button"
                            onClick={() => handleSaveInstructions(itemId, item.quantity, instructionsValue)}
                            className={styles.saveButton}
                            disabled={state.isSyncing}
                          >
                            {t('save', 'Save')}
                          </button>
                          <button
                            type="button"
                            onClick={() => {
                              setEditingInstructions(null);
                              setInstructionsValue('');
                            }}
                            className={styles.cancelButton}
                          >
                            {t('cancel', 'Cancel')}
                          </button>
                        </div>
                      </div>
                    ) : (
                      <>
                        {item.specialInstructions && (
                          <p className={styles.instructionsText}>
                            <strong>{t('special_instructions', 'Special Instructions')}:</strong> {item.specialInstructions}
                          </p>
                        )}
                        <button
                          type="button"
                          onClick={() => {
                            setEditingInstructions(itemId || null);
                            setInstructionsValue(item.specialInstructions || '');
                          }}
                          className={styles.editInstructionsButton}
                          disabled={state.isSyncing}
                        >
                          {item.specialInstructions ? t('edit_instructions', 'Edit Instructions') : t('add_instructions', 'Add Instructions')}
                        </button>
                      </>
                    )}
                    </div>
                  )}
                </div>

                {/* Item Controls */}
                <div className={styles.itemControls}>
                  <div className={styles.quantityControl}>
                    <button
                      type="button"
                      onClick={() => handleUpdateQuantity(itemId, item.quantity - 1)}
                      className={styles.quantityButton}
                      disabled={state.isSyncing || item.quantity <= 1}
                      aria-label={t('decrease_quantity', 'Decrease quantity')}
                    >
                      <Minus size={16} />
                    </button>
                    <span className={styles.itemQuantity}>{item.quantity}</span>
                    <button
                      type="button"
                      onClick={() => handleUpdateQuantity(itemId, item.quantity + 1)}
                      className={styles.quantityButton}
                      disabled={state.isSyncing}
                      aria-label={t('increase_quantity', 'Increase quantity')}
                    >
                      <Plus size={16} />
                    </button>
                  </div>

                  <p className={styles.itemSubtotal}>
                    CHF {item.itemTotal.toFixed(2)}
                  </p>

                  {/* Remove Button */}
                  <button
                    type="button"
                    onClick={() => handleRemoveItem(itemId)}
                    className={styles.removeItemButton}
                    disabled={state.isSyncing}
                    aria-label={t('remove_item', 'Remove item')}
                  >
                    <Trash2 size={16} />
                    <span>{t('remove', 'Remove')}</span>
                  </button>
                </div>
              </div>
            );
          })}
        </div>

        {/* Cart Summary */}
        <div className={styles.cartSummary}>
          {/* Promo Code Section */}
          <div className={styles.promoCodeSection}>
            <h3 className={styles.promoCodeTitle}>
              <Tag size={20} />
              {t('promo_code', 'Promo Code')}
            </h3>

            {state.basket?.promoCode ? (
              <div className={styles.appliedPromoCode}>
                <div className={styles.promoCodeInfo}>
                  <span className={styles.promoCodeText}>{state.basket.promoCode}</span>
                  <span className={styles.promoCodeDiscount}>
                    -{t('discount', 'Discount')}: CHF {state.basket.discount.toFixed(2)}
                  </span>
                </div>
                <button
                  onClick={handleRemovePromoCode}
                  className={styles.removePromoButton}
                  disabled={state.isSyncing}
                >
                  {t('remove', 'Remove')}
                </button>
              </div>
            ) : (
              <div className={styles.promoCodeInput}>
                <input
                  type="text"
                  value={promoCode}
                  onChange={(e) => setPromoCode(e.target.value)}
                  placeholder={t('enter_promo_code', 'Enter promo code')}
                  className={styles.promoCodeField}
                  disabled={state.isSyncing}
                  onKeyPress={(e) => {
                    if (e.key === 'Enter') {
                      handleApplyPromoCode();
                    }
                  }}
                />
                <button
                  onClick={handleApplyPromoCode}
                  className={styles.applyPromoButton}
                  disabled={state.isSyncing || !promoCode.trim()}
                >
                  {isApplyingPromo ? (
                    <Loader2 className={styles.buttonSpinner} size={16} />
                  ) : (
                    t('apply', 'Apply')
                  )}
                </button>
              </div>
            )}
          </div>

          {/* Price Breakdown */}
          <div className={styles.priceBreakdown}>
            <div className={styles.priceRow}>
              <span>{t('subtotal', 'Subtotal')}:</span>
              <span>CHF {state.basket?.subTotal.toFixed(2) || '0.00'}</span>
            </div>

            {state.basket && state.basket.discount > 0 && (
              <div className={styles.priceRow}>
                <span>{t('discount', 'Discount')}:</span>
                <span className={styles.discountAmount}>-CHF {state.basket.discount.toFixed(2)}</span>
              </div>
            )}

            <div className={styles.priceRow}>
              <span>{t('tax', 'Tax')}:</span>
              <span>CHF {state.basket?.tax.toFixed(2) || '0.00'}</span>
            </div>

            <div className={styles.totalRow}>
              <span>{t('total', 'Total')}:</span>
              <span className={styles.totalAmount}>CHF {getTotal().toFixed(2)}</span>
            </div>
          </div>

          {/* Checkout Button */}
          <Link href="/checkout/order-type" className={styles.checkoutButton}>
            {t('proceed_to_checkout', 'Proceed to Checkout')} ({getItemCount()} {t('items', 'items')})
          </Link>
        </div>
      </div>
    </main>
  );
}
