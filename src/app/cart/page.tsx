"use client";

import React from 'react';
import Link from 'next/link';
import { useCart } from '@/components/cart/CartContext';
import styles from '../styles/CartPage.module.css';
import { useTranslation } from 'react-i18next';

export default function CartPage() {
  const { state, dispatch } = useCart();
  const { t } = useTranslation();

  const handleRemoveItem = (id: string) => {
    dispatch({ type: 'REMOVE_ITEM', payload: { id } });
  };

  const handleUpdateQuantity = (id: string, quantity: number) => {
    if (quantity < 1) return; // Prevent negative or zero quantity
    dispatch({ type: 'UPDATE_QUANTITY', payload: { id, quantity } });
  };

  const totalPrice = state.items.reduce((sum, item) => sum + item.price * item.quantity, 0);

  if (state.items.length === 0) {
    return (
      <main className={styles.cartContainer} aria-labelledby="cart-heading">
        <h1 id="cart-heading" className={styles.pageTitle}>{t('cart_title')}</h1>
        <div className={styles.emptyCartContainer}>
          <p className={styles.emptyCartMessage}>{t('cart_empty_message')}</p>
          <Link href="/menu" className={styles.emptyCartLink}>
            {t('cart_browse_menu_button')}
          </Link>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.cartContainer} aria-labelledby="cart-heading">
      <h1 id="cart-heading" className={styles.pageTitle}>{t('cart_title')}</h1>
      <div className={styles.cartItemsList}>
        {state.items.map((item) => (
          <div key={item.id} className={styles.cartItem}>
            <div className={styles.itemDetails}>
              <h2 className={styles.itemName}>{item.name}</h2>
              <p className={styles.itemPrice}>CHF {item.price.toFixed(2)}</p>
            </div>
            <div className={styles.itemControls}>
              <div className={styles.quantityControl}>
                <button onClick={() => handleUpdateQuantity(item.id, item.quantity - 1)} className={styles.quantityButton}>-</button>
                <span className={styles.itemQuantity}>{item.quantity}</span>
                <button onClick={() => handleUpdateQuantity(item.id, item.quantity + 1)} className={styles.quantityButton}>+</button>
              </div>
              <button onClick={() => handleRemoveItem(item.id)} className={styles.removeItemButton}>
                {t('cart_remove_item_button')}
              </button>
            </div>
            <p className={styles.itemSubtotal}>{t('subtotal_label', 'Subtotal')}: CHF {(item.price * item.quantity).toFixed(2)}</p>
          </div>
        ))}
      </div>
      <div className={styles.cartSummary}>
        <h2 className={styles.totalPrice}>{t('cart_total_price_label')}: CHF {totalPrice.toFixed(2)}</h2>
        <Link href="/checkout" className={styles.checkoutButton}>
          {t('cart_proceed_to_checkout_button')}
        </Link>
      </div>
    </main>
  );
}
