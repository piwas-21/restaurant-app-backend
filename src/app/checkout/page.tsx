// src/app/checkout/page.tsx
"use client";

import React, { useState } from 'react';
import { useCart } from '@/components/cart/CartContext';
import styles from "../styles/CheckoutPage.module.css";
import Cart from "@/components/cart/Cart";
import Link from 'next/link';
import { useTranslation } from 'react-i18next';

export default function CheckoutPage() {
  const { state } = useCart(); 
  const { t } = useTranslation();
  const [orderType, setOrderType] = useState<'pickup' | 'dine-in'>('pickup');
  const [tableNumber, setTableNumber] = useState('');
  const [tipPercentage, setTipPercentage] = useState(0);
  const [customTip, setCustomTip] = useState('');
  const [customerName, setCustomerName] = useState(''); 
  const [customerPhone, setCustomerPhone] = useState(''); 
  const [isProcessingPayment, setIsProcessingPayment] = useState(false);

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

  const handlePayrexxPayment = async () => {
    if (!customerName || !customerPhone) { 
        alert(t('checkout_place_order_pickup_validation')); 
        return;
    }

    setIsProcessingPayment(true);
    const referenceId = `RUMI-ORDER-${Date.now()}`;
    const paymentData = {
      amount: total, 
      currency: 'CHF', 
      referenceId: referenceId,
      successRedirectUrl: `${window.location.origin}/payment-success?orderId=${referenceId}`,
      failedRedirectUrl: `${window.location.origin}/payment-failed?orderId=${referenceId}`,
      cancelRedirectUrl: `${window.location.origin}/checkout`,
      customer: {
        firstName: customerName.split(' ')[0] || 'Rumi',
        lastName: customerName.split(' ').slice(1).join(' ') || 'Customer',
        email: 'default@example.com', 
        phone: customerPhone,
      },
    };

    try {
      const response = await fetch('/api/create-payment', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(paymentData),
      });

      if (!response.ok) {
        const errorResult = await response.json();
        throw new Error(errorResult.error || 'Payment initiation failed.');
      }

      const result = await response.json();
      if (result.link) {
        console.log("Order details to be saved (desktop) before redirect:", {
            referenceId,
            items: state.items,
            orderType,
            tableNumber, 
            customerName,
            customerPhone,
            subtotal: subtotal.toFixed(2),
            tipAmount: tipAmount.toFixed(2),
            total: total.toFixed(2),
            paymentStatus: 'pending_payrexx'
        });
        window.location.href = result.link;
      } else {
        throw new Error('No payment link received from Payrexx.');
      }
    } catch (error) {
      console.error("Payrexx payment error (desktop):", error);
      alert(`Error: ${error instanceof Error ? error.message : 'Could not initiate Payrexx payment.'}`);
      setIsProcessingPayment(false);
    }
  };

  // const handlePlaceCashOrder = (e: React.FormEvent) => {
  //   e.preventDefault();
  //    if (!customerName || !customerPhone) {
  //       alert(t('checkout_place_order_pickup_validation'));
  //       return;
  //   }
  //   if (orderType === 'dine-in' && !tableNumber) {
  //       alert(t('checkout_place_order_dine_in_validation'));
  //       return;
  //   }
  //   console.log("Order Placed (Cash/Card):", {
  //     items: state.items, orderType, tableNumber, customerName, customerPhone, subtotal, tipAmount, total,
  //     paymentMethod: "Cash/Card on Pickup/Delivery"
  //   });
  //   alert(t('checkout_order_placed_success'));
  // };


  if (state.items.length === 0 && !isProcessingPayment) {
    return (
      <main className={styles.checkoutContainer} aria-labelledby="checkout-heading">
        <h1 id="checkout-heading">{t('checkout_title')}</h1>
        <p>{t('checkout_empty_cart_message_1')} <Link href="/menu">{t('checkout_empty_cart_message_2')}</Link> {t('checkout_empty_cart_message_3')}</p>
      </main>
    );
  }

  return (
    <main className={styles.checkoutContainer} aria-labelledby="checkout-main-heading">
      {isProcessingPayment && <div className={styles.overlay}><p>{t('checkout_processing_payment')}...</p></div>}
      <h1 id="checkout-main-heading">{t('checkout_title')}</h1>
      
      <section className={styles.cartSummarySection} aria-labelledby="order-summary-heading">
        <h2 id="order-summary-heading">{t('checkout_order_summary')}</h2>
        <Cart showProceedButton={false} /> {/* Pass prop to hide the button */}
      </section>

      <div> 
        <section className={styles.orderDetailsSection} aria-labelledby="order-details-heading">
          <h2 id="order-details-heading">{t('checkout_order_details')}</h2>
          
          <div className={styles.formGroup}>
            <label htmlFor="customerName">{t('checkout_customer_name_label')}</label>
            <input
              type="text"
              id="customerName"
              value={customerName}
              onChange={(e) => setCustomerName(e.target.value)}
              className={styles.textInput}
              placeholder={t('checkout_customer_name_placeholder')}
              required
              disabled={isProcessingPayment}
            />
          </div>
          <div className={styles.formGroup}>
            <label htmlFor="customerPhone">{t('checkout_customer_phone_label')}</label>
            <input
              type="tel"
              id="customerPhone"
              value={customerPhone}
              onChange={(e) => setCustomerPhone(e.target.value)}
              className={styles.textInput}
              placeholder={t('checkout_customer_phone_placeholder')}
              required
              disabled={isProcessingPayment}
            />
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="orderType">{t('checkout_order_type_label')}</label>
            <select 
              id="orderType" 
              value={orderType} 
              onChange={(e) => setOrderType(e.target.value as 'pickup' | 'dine-in')} 
              className={styles.selectInput}
              disabled={isProcessingPayment} 
            >
              <option value="pickup">{t('checkout_order_type_pickup')}</option>
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
                aria-required={orderType === 'dine-in'}
                disabled={isProcessingPayment}
              />
            </div>
          )}
        </section>

        <section className={styles.tipSection} aria-labelledby="tip-heading">
          <h2 id="tip-heading">{t('checkout_add_tip_label')}</h2>
          <div className={styles.tipOptions} role="group" aria-label={t('checkout_tip_options_aria_label')}>
            <button type="button" onClick={() => { setTipPercentage(10); setCustomTip(''); }} className={tipPercentage === 10 && !customTip ? styles.activeTip : ''} aria-pressed={tipPercentage === 10 && !customTip} disabled={isProcessingPayment}>10%</button>
            <button type="button" onClick={() => { setTipPercentage(15); setCustomTip(''); }} className={tipPercentage === 15 && !customTip ? styles.activeTip : ''} aria-pressed={tipPercentage === 15 && !customTip} disabled={isProcessingPayment}>15%</button>
            <button type="button" onClick={() => { setTipPercentage(20); setCustomTip(''); }} className={tipPercentage === 20 && !customTip ? styles.activeTip : ''} aria-pressed={tipPercentage === 20 && !customTip} disabled={isProcessingPayment}>20%</button>
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
              disabled={isProcessingPayment}
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
            <h2 id="payment-options-heading" className={styles.srOnly}>{t('checkout_payment_options_label')}</h2>
            <div className={styles.paymentButtonsContainer}>
                <button 
                  type="button" 
                  onClick={handlePayrexxPayment} 
                  className={`${styles.paymentButton} ${styles.payrexxButton}`}
                  aria-label={t('checkout_payrexx_aria_label')}
                  disabled={isProcessingPayment || state.items.length === 0}
                >
                  {isProcessingPayment ? t('checkout_processing_payment') : t('checkout_pay_with_payrexx')}
                </button>
            </div>
        </section>
      </div>
    </main>
  );
}
