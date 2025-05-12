// src/components/cart/Cart.tsx
"use client";

import React from 'react';
import { useCart } from './CartContext';
import styles from "../../app/styles/Cart.module.css"; // Create this CSS module
import Link from 'next/link';

export default function Cart() {
  const { state, dispatch } = useCart();

  const handleRemoveItem = (itemId: string, itemName: string) => {
    console.log(`Removing item: ${itemName}`);
    dispatch({ type: 'REMOVE_ITEM', payload: { id: itemId } });
    // Announce removal for screen readers if possible, or ensure focus management guides user
  };

  const handleUpdateQuantity = (itemId: string, quantity: number, itemName: string) => {
    console.log(`Updating quantity for item: ${itemName} to ${quantity}`);
    dispatch({ type: 'UPDATE_QUANTITY', payload: { id: itemId, quantity } });
  };

  const calculateTotal = () => {
    return state.items.reduce((total, item) => total + item.price * item.quantity, 0).toFixed(2);
  };

  if (state.items.length === 0) {
    return (
      <div className={styles.cartContainer} role="region" aria-labelledby="cart-heading">
        <h2 id="cart-heading">Your Cart</h2>
        <p>Your cart is empty. <Link href="/menu">Browse our menu</Link> to add items.</p>
      </div>
    );
  }

  return (
    <div className={styles.cartContainer} role="region" aria-labelledby="cart-heading">
      <h2 id="cart-heading">Your Cart</h2>
      <ul className={styles.cartItemsList} aria-label="Items in your cart">
        {state.items.map((item) => (
          <li key={item.id} className={styles.cartItem} role="listitem">
            <div className={styles.itemInfo}>
              <span>{item.name} (CHF {item.price.toFixed(2)})</span>
            </div>
            <div className={styles.itemControls}>
              <label htmlFor={`quantity-${item.id}`} className="sr-only">Quantity for {item.name}</label>
              <input
                type="number"
                id={`quantity-${item.id}`}
                value={item.quantity}
                onChange={(e) => handleUpdateQuantity(item.id, parseInt(e.target.value, 10), item.name)}
                min="1"
                className={styles.quantityInput}
                aria-label={`Quantity for ${item.name}`}
              />
              <button 
                onClick={() => handleRemoveItem(item.id, item.name)} 
                className={styles.removeButton}
                aria-label={`Remove ${item.name} from cart`}
              >
                Remove
              </button>
            </div>
          </li>
        ))}
      </ul>
      <div className={styles.cartTotal} role="status" aria-live="polite">
        <h3>Total: CHF {calculateTotal()}</h3>
      </div>
      <Link href="/checkout" className={styles.checkoutButton} role="button">
        Proceed to Checkout
      </Link>
      {/* Add tip functionality later */}
    </div>
  );
}

// Add a visually hidden class for sr-only labels if not already in globals.css
// .sr-only {
//   position: absolute;
//   width: 1px;
//   height: 1px;
//   padding: 0;
//   margin: -1px;
//   overflow: hidden;
//   clip: rect(0, 0, 0, 0);
//   white-space: nowrap;
//   border-width: 0;
// }

