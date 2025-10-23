// src/components/cart/Cart.tsx
"use client";

import React from 'react';
import { useTranslation } from 'react-i18next';
import { useCart } from './CartContext';
import styles from "../../app/styles/Cart.module.css";
import Link from 'next/link';

interface CartProps {
  showProceedButton?: boolean;
}

export default function Cart({ showProceedButton = true }: CartProps) {
  const { t } = useTranslation();
  const { state, removeItem, updateItem, getTotal } = useCart();

  const handleRemoveItem = async (basketItemId: string | undefined, itemName: string) => {
    if (!basketItemId) return;
    try {
      await removeItem(basketItemId);
    } catch {
      // Error already shown via CartContext
      // eslint-disable-next-line no-console
      console.error(`Failed to remove item: ${itemName}`);
    }
  };

  const handleUpdateQuantity = async (basketItemId: string | undefined, quantity: number, itemName: string) => {
    if (!basketItemId || quantity < 1) return;
    try {
      await updateItem(basketItemId, quantity);
    } catch {
      // Error already shown via CartContext
      // eslint-disable-next-line no-console
      console.error(`Failed to update quantity for item: ${itemName}`);
    }
  };

  if (state.items.length === 0) {
    return (
      <div className={styles.cartContainer} role="region" aria-labelledby="cart-heading-empty">
        <h2 id="cart-heading-empty">{t("cart_title", 'Your Cart')}</h2>
        <p>{t('cart_empty_message', 'Your cart is empty.')} <Link href="/menu">{t('browse_our_menu', 'Browse our menu')}</Link> {t('to_add_items', 'to add items')}.</p>
      </div>
    );
  }

  return (
    <div className={styles.cartContainer} role="region" aria-labelledby="cart-heading-full">
      <h2 id="cart-heading-full">{t("cart_title", 'Your Cart')}</h2>
      {state.isLoading && <p>{t('loading', 'Loading...')}</p>}
      {state.error && <p className={styles.error}>{state.error}</p>}
      <ul className={styles.cartItemsList} aria-label="Items in your cart">
        {state.items.map((item) => (
          <li key={item.id || item.productId} className={styles.cartItem} role="listitem">
            <div className={styles.itemInfo}>
              <span>{item.productName || 'Unknown Item'} (CHF {item.unitPrice.toFixed(2)})</span>
              {item.specialInstructions && (
                <p className={styles.instructions}>{item.specialInstructions}</p>
              )}
            </div>
            <div className={styles.itemControls}>
              <label htmlFor={`quantity-${item.id}`} className="sr-only">{t('quantity_for', ' Quantity for')} {item.productName}</label>
              <input
                type="number"
                id={`quantity-${item.id}`}
                value={item.quantity}
                onChange={(e) => handleUpdateQuantity(item.id, parseInt(e.target.value, 10), item.productName || 'Unknown')}
                min="1"
                className={styles.quantityInput}
                aria-label={`Quantity for ${item.productName}`}
                disabled={state.isSyncing}
              />
              <button
                onClick={() => handleRemoveItem(item.id, item.productName || 'Unknown')}
                className={styles.removeButton}
                aria-label={`Remove ${item.productName} from cart`}
                disabled={state.isSyncing}
              >
                {t('cart_remove_item_button', 'Remove')}
              </button>
            </div>
          </li>
        ))}
      </ul>
      <div className={styles.cartTotal} role="status" aria-live="polite">
        <h3>{t('total_price_header', 'Total')}: CHF {getTotal().toFixed(2)}</h3>
      </div>
      {showProceedButton && (
        <Link href="/checkout" className={styles.checkoutButton} role="button">
          {t('cart_proceed_to_checkout_button', 'Proceed to Checkout')}
        </Link>
      )}
    </div>
  );
}
