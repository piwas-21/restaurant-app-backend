// src/app/checkout/page.tsx
"use client";

import React, { useState } from 'react';
import { useCart } from '@/components/cart/CartContext'; // Adjusted import path
import styles from "../styles/CheckoutPage.module.css"; // Create this CSS module
import Cart from "@/components/cart/Cart"; // Adjusted import path
import Link from 'next/link';
import { useTranslation } from 'react-i18next'; // Import useTranslation

export default function CheckoutPage() {
  const { state } = useCart();
  const { t } = useTranslation(); // Initialize useTranslation
  const [orderType, setOrderType] = useState<'pickup' | 'dine-in'>('pickup');
  const [tableNumber, setTableNumber] = useState('');
  const [tipPercentage, setTipPercentage] = useState(0);
  const [customTip, setCustomTip] = useState('');

  const calculateSubtotal = () => {
    return state.items.reduce((total, item) => total + item.price * item.quantity, 0);
  };

  const subtotal = calculateSubtotal();
  let tipAmount = 0;
  if (customTip) {
    tipAmount = parseFloat(customTip) || 0;
  } else if (tipPercentage > 0) {
    tipAmount = subtotal * (tipPercentage / 100);
  }

  const total = subtotal + tipAmount;

  const handlePlaceOrder = (e: React.FormEvent) => {
    e.preventDefault();
    console.log("Order Placed:", {
      items: state.items,
      orderType,
      tableNumber: orderType === 'dine-in' ? tableNumber : undefined,
      subtotal: subtotal.toFixed(2),
      tipAmount: tipAmount.toFixed(2),
      total: total.toFixed(2),
      paymentMethod: "Cash/Card on Pickup/Delivery" // Placeholder
    });
    alert(t('checkout_order_placed_success'));
  };

  const handleGooglePay = () => {
    alert(t('checkout_google_pay_placeholder'));
    // Placeholder for Google Pay integration
  };

  const handleApplePay = () => {
    alert(t('checkout_apple_pay_placeholder'));
    // Placeholder for Apple Pay integration
  };

  if (state.items.length === 0) {
    return (
      <main className={styles.checkoutContainer} aria-labelledby="checkout-heading">
        <h1 id="checkout-heading">{t('checkout_title')}</h1>
        <p>{t('checkout_empty_cart_message_1')} <Link href="/menu">{t('checkout_empty_cart_message_2')}</Link> {t('checkout_empty_cart_message_3')}</p>
      </main>
    );
  }

  return (
    <main className={styles.checkoutContainer} aria-labelledby="checkout-main-heading">
      <h1 id="checkout-main-heading">{t('checkout_title')}</h1>
      
      <section className={styles.cartSummarySection} aria-labelledby="order-summary-heading">
        <h2 id="order-summary-heading">{t('checkout_order_summary')}</h2>
        <Cart />
      </section>

      <form onSubmit={handlePlaceOrder}>
        <section className={styles.orderDetailsSection} aria-labelledby="order-details-heading">
          <h2 id="order-details-heading">{t('checkout_order_details')}</h2>
          <div className={styles.formGroup}>
            <label htmlFor="orderType">{t('checkout_order_type_label')}</label>
            <select 
              id="orderType" 
              value={orderType} 
              onChange={(e) => setOrderType(e.target.value as 'pickup' | 'dine-in')} 
              className={styles.selectInput}
            >
              <option value="pickup">{t('checkout_order_type_pickup')}</option>
              <option value="dine-in">{t('checkout_order_type_dine_in')}</option>
            </select>
          </div>

          {orderType === 'dine-in' && (
            <div className={styles.formGroup}>
              <label htmlFor="tableNumber">{t('checkout_table_number_label')}</label>
              <input
                type="text"
                id="tableNumber"
                value={tableNumber}
                onChange={(e) => setTableNumber(e.target.value)}
                className={styles.textInput}
                placeholder={t('checkout_table_number_placeholder')}
                aria-required="true"
              />
            </div>
          )}
        </section>

        <section className={styles.tipSection} aria-labelledby="tip-heading">
          <h2 id="tip-heading">{t('checkout_add_tip_label')}</h2>
          <div className={styles.tipOptions} role="group" aria-label={t('checkout_tip_options_aria_label')}>
            <button type="button" onClick={() => { setTipPercentage(10); setCustomTip(''); }} className={tipPercentage === 10 && !customTip ? styles.activeTip : ''} aria-pressed={tipPercentage === 10 && !customTip}>10%</button>
            <button type="button" onClick={() => { setTipPercentage(15); setCustomTip(''); }} className={tipPercentage === 15 && !customTip ? styles.activeTip : ''} aria-pressed={tipPercentage === 15 && !customTip}>15%</button>
            <button type="button" onClick={() => { setTipPercentage(20); setCustomTip(''); }} className={tipPercentage === 20 && !customTip ? styles.activeTip : ''} aria-pressed={tipPercentage === 20 && !customTip}>20%</button>
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="customTip">{t('checkout_custom_tip_label')}</label>
            <input
              type="number"
              id="customTip"
              value={customTip}
              onChange={(e) => { setCustomTip(e.target.value); setTipPercentage(0); }}
              className={styles.textInput}
              placeholder={t('checkout_custom_tip_placeholder')}
              aria-label={t('checkout_custom_tip_aria_label')}
              min="0"
              step="0.01"
            />
          </div>
        </section>

        <section className={styles.finalTotalSection} aria-live="polite" role="region" aria-labelledby="final-total-heading">
          <h3 id="final-total-heading" className="sr-only">{t('checkout_final_total_sr_only')}</h3>
          <p>{t('checkout_subtotal_label')} CHF {subtotal.toFixed(2)}</p>
          <p>{t('checkout_tip_label')} CHF {tipAmount.toFixed(2)}</p>
          <p><strong>{t('checkout_total_label')} CHF {total.toFixed(2)}</strong></p>
        </section>

        <section className={styles.paymentOptionsSection} aria-labelledby="payment-options-heading">
            <h2 id="payment-options-heading">{t('checkout_payment_options_label')}</h2>
            <div className={styles.paymentButtonsContainer}>
                <button type="button" onClick={handleGooglePay} className={`${styles.paymentButton} ${styles.googlePayButton}`} aria-label={t('checkout_google_pay_aria_label')}>
                    {/* Placeholder for Google Pay Button Icon */}
                    <span>{t('checkout_pay_with_google_pay')}</span>
                </button>
                <button type="button" onClick={handleApplePay} className={`${styles.paymentButton} ${styles.applePayButton}`} aria-label={t('checkout_apple_pay_aria_label')}>
                    {/* Placeholder for Apple Pay Button Icon */}
                    <span>{t('checkout_pay_with_apple_pay')}</span>
                </button>
            </div>
        </section>

        <button type="submit" className={styles.placeOrderButton}>
          {t('checkout_place_order_button')}
        </button>
      </form>
    </main>
  );
}

