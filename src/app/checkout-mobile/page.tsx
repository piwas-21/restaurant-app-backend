// src/app/checkout-mobile/page.tsx
"use client";

import React, { useState } from "react";
// import type { Metadata } from "next"; // Static metadata
import styles from "../styles/CheckoutMobile.module.css";
import { useCart } from "@/components/cart/CartContext";
import Link from "next/link";
import { useTranslation } from "react-i18next"; // Import useTranslation

// export const metadata: Metadata = {
//   title: "Checkout - RUMI Restaurant Mobile",
//   description: "Complete your order at RUMI Restaurant. Choose pickup or dine-in options.",
// };

export default function CheckoutMobilePage() {
  const { t } = useTranslation(); // Initialize useTranslation
  const { state } = useCart();
  const [orderType, setOrderType] = useState<
    "pickup" | "dine-in"
  >("pickup");
  const [tableNumber, setTableNumber] = useState("");
  const [tipPercentage, setTipPercentage] = useState(0);
  const [customTip, setCustomTip] = useState("");
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");

  const calculateSubtotal = () => {
    return state.items.reduce(
      (total, item) => total + item.price * item.quantity,
      0
    );
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
    if (orderType === "pickup" && (!name || !phone)) {
      alert(t("place_order_pickup_validation"));
      return;
    }
    if (orderType === "dine-in" && !tableNumber) {
      alert(t("place_order_dine_in_validation"));
      return;
    }

    console.log("Mobile Order Placed:", {
      items: state.items,
      orderType,
      tableNumber: orderType === "dine-in" ? tableNumber : undefined,
      customerName: orderType === "pickup" ? name : undefined,
      customerPhone: orderType === "pickup" ? phone : undefined,
      subtotal: subtotal.toFixed(2),
      tipAmount: tipAmount.toFixed(2),
      total: total.toFixed(2),
    });
    alert(t("order_placed_success"));
    // dispatch({ type: "CLEAR_CART" });
  };

  if (state.items.length === 0) {
    return (
      <main className={styles.checkoutMobileContainer} aria-labelledby="checkout-heading">
        <header className={styles.checkoutHeader}>
          <Link href="/cart-mobile" className={styles.backButton} aria-label={t("back_to_cart")}>
            &larr;
          </Link>
          <h1 id="checkout-heading">{t("checkout_title")}</h1>
          <span style={{width: "40px"}}></span>
        </header>
        <div className={styles.emptyMessageContainer}>
          <p>{t("checkout_empty_cart_message")}</p>
          <Link href="/menu-mobile" className={styles.actionButton}>
            {t("go_to_menu_button")}
          </Link>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.checkoutMobileContainer} aria-labelledby="checkout-main-heading">
      <header className={styles.checkoutHeader}>
        <Link href="/cart-mobile" className={styles.backButton} aria-label={t("back_to_cart")}>
          &larr;
        </Link>
        <h1 id="checkout-main-heading">{t("checkout_confirm_order")}</h1>
        <span style={{width: "40px"}}></span>
      </header>

      <form onSubmit={handlePlaceOrder} className={styles.checkoutFormMobile}>
        <section className={styles.orderSummarySectionMobile} aria-labelledby="summary-heading">
          <h2 id="summary-heading">{t("order_summary")}</h2>
          {state.items.map(item => (
            <div key={item.id} className={styles.summaryItemMobile}>
              <span>{item.name} (x{item.quantity})</span>
              <span>CHF {(item.price * item.quantity).toFixed(2)}</span>
            </div>
          ))}
          <div className={styles.summaryTotalMobile}>
            <strong>{t("subtotal", { amount: subtotal.toFixed(2) })}</strong>
          </div>
        </section>

        <section className={styles.orderDetailsSectionMobile} aria-labelledby="details-heading">
          <h2 id="details-heading">{t("order_details")}</h2>
          <div className={styles.formGroupMobile}>
            <label htmlFor="orderTypeMobile">{t("order_type")}</label>
            <select
              id="orderTypeMobile"
              value={orderType}
              onChange={(e) =>
                setOrderType(e.target.value as "pickup" | "dine-in")
              }
              className={styles.selectInputMobile}
            >
              <option value="pickup">{t("pickup")}</option>
              <option value="dine-in">{t("dine_in")}</option>
            </select>
          </div>

          {orderType === "pickup" && (
            <>
              <div className={styles.formGroupMobile}>
                <label htmlFor="customerNameMobile">{t("customer_name_label")}</label>
                <input
                  type="text"
                  id="customerNameMobile"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  className={styles.textInputMobile}
                  placeholder={t("customer_name_placeholder")}
                  required
                />
              </div>
              <div className={styles.formGroupMobile}>
                <label htmlFor="customerPhoneMobile">{t("customer_phone_label")}</label>
                <input
                  type="tel"
                  id="customerPhoneMobile"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  className={styles.textInputMobile}
                  placeholder={t("customer_phone_placeholder")}
                  required
                />
              </div>
            </>
          )}

          {orderType === "dine-in" && (
            <div className={styles.formGroupMobile}>
              <label htmlFor="tableNumberMobile">{t("table_number_label")}</label>
              <input
                type="text"
                id="tableNumberMobile"
                value={tableNumber}
                onChange={(e) => setTableNumber(e.target.value)}
                className={styles.textInputMobile}
                placeholder={t("table_number_placeholder")}
                required
              />
            </div>
          )}
        </section>

        <section className={styles.tipSectionMobile} aria-labelledby="tip-heading-mobile">
          <h2 id="tip-heading-mobile">{t("add_a_tip")}</h2>
          <div className={styles.tipOptionsMobile} role="group" aria-label={t("tip_percentage_options")}>
            {[10, 15, 20].map(perc => (
                <button 
                    key={perc} 
                    type="button" 
                    onClick={() => { setTipPercentage(perc); setCustomTip(""); }} 
                    className={`${styles.tipButtonMobile} ${tipPercentage === perc && !customTip ? styles.activeTipMobile : ""}`}
                    aria-pressed={tipPercentage === perc && !customTip}
                >
                    {perc}%
                </button>
            ))}
          </div>
          <div className={styles.formGroupMobile}>
            <label htmlFor="customTipMobile">{t("custom_tip_label")}</label>
            <input
              type="number"
              id="customTipMobile"
              value={customTip}
              onChange={(e) => {
                setCustomTip(e.target.value);
                setTipPercentage(0);
              }}
              className={styles.textInputMobile}
              placeholder={t("custom_tip_placeholder")}
              min="0"
              step="0.01"
            />
          </div>
        </section>

        <div className={styles.finalTotalSectionMobile} aria-live="polite">
          <p>{t("final_total_subtotal")} <span className={styles.amount}>CHF {subtotal.toFixed(2)}</span></p>
          <p>{t("final_total_tip")} <span className={styles.amount}>CHF {tipAmount.toFixed(2)}</span></p>
          <p className={styles.grandTotal}>{t("final_total_grand")} <span className={styles.amountBold}>CHF {total.toFixed(2)}</span></p>
        </div>

        <button type="submit" className={styles.placeOrderButtonMobile}>
          {t("place_order_button")}
        </button>
      </form>
    </main>
  );
}

