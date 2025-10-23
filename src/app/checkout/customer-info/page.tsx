"use client";

import React, { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useCheckout } from '@/contexts/CheckoutContext';
import { useCart } from '@/components/cart/CartContext';
import {
  User,
  Mail,
  Phone,
  ChevronRight,
  AlertCircle
} from 'lucide-react';
import styles from '../../styles/CustomerInfoPage.module.css';

export default function CustomerInfoPage() {
  const { t } = useTranslation();
  const router = useRouter();
  const { state: checkoutState, setCustomerInfo } = useCheckout();
  const { state: cartState } = useCart();

  // Form state
  const [name, setName] = useState(checkoutState.customerInfo?.name || '');
  const [email, setEmail] = useState(checkoutState.customerInfo?.email || '');
  const [phone, setPhone] = useState(checkoutState.customerInfo?.phone || '');

  // Error state
  const [nameError, setNameError] = useState('');
  const [emailError, setEmailError] = useState('');
  const [phoneError, setPhoneError] = useState('');

  // Save preference state
  const [saveInfo, setSaveInfo] = useState(false);

  // Load saved customer info from localStorage on mount
  useEffect(() => {
    if (typeof window !== 'undefined') {
      const saved = localStorage.getItem('rumi_saved_customer_info');
      if (saved && !checkoutState.customerInfo) {
        try {
          const parsed = JSON.parse(saved);
          setName(parsed.name || '');
          setEmail(parsed.email || '');
          setPhone(parsed.phone || '');
        } catch {
          // Ignore parse errors
        }
      }
    }
  }, [checkoutState.customerInfo]);

  // Check if cart is empty
  if (cartState.items.length === 0) {
    return (
      <main className={styles.container}>
        <div className={styles.emptyState}>
          <h1>{t('checkout_title', 'Checkout')}</h1>
          <p>{t('cart_empty_message', 'Your cart is empty')}</p>
          <button
            onClick={() => router.push('/menu')}
            className={styles.browseButton}
          >
            {t('cart_browse_menu_button', 'Browse Menu')}
          </button>
        </div>
      </main>
    );
  }

  // Check if order type is selected
  if (!checkoutState.orderType) {
    return (
      <main className={styles.container}>
        <div className={styles.emptyState}>
          <AlertCircle size={64} className={styles.alertIcon} />
          <h1>{t('order_type_not_selected', 'Order Type Not Selected')}</h1>
          <p>{t('order_type_not_selected_desc', 'Please select your order type first')}</p>
          <button
            onClick={() => router.push('/checkout/order-type')}
            className={styles.browseButton}
          >
            {t('select_order_type', 'Select Order Type')}
          </button>
        </div>
      </main>
    );
  }

  const validateName = (value: string): boolean => {
    if (!value.trim()) {
      setNameError(t('name_required', 'Name is required'));
      return false;
    }
    if (value.trim().length < 2) {
      setNameError(t('name_too_short', 'Name must be at least 2 characters'));
      return false;
    }
    if (value.trim().length > 100) {
      setNameError(t('name_too_long', 'Name must be less than 100 characters'));
      return false;
    }
    setNameError('');
    return true;
  };

  const validateEmail = (value: string): boolean => {
    if (!value.trim()) {
      setEmailError(t('email_required', 'Email is required'));
      return false;
    }
    // Email regex pattern
    const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    if (!emailPattern.test(value.trim())) {
      setEmailError(t('email_invalid', 'Please enter a valid email address'));
      return false;
    }
    setEmailError('');
    return true;
  };

  const validatePhone = (value: string): boolean => {
    if (!value.trim()) {
      setPhoneError(t('phone_required', 'Phone number is required'));
      return false;
    }
    // Swiss phone number pattern (flexible format)
    // Accepts: +41 XX XXX XX XX, 0XX XXX XX XX, etc.
    const phonePattern = /^(\+41|0041|0)?[\s]?[1-9]\d{1,2}[\s]?\d{3}[\s]?\d{2}[\s]?\d{2}$/;
    const cleanedPhone = value.replace(/[\s-]/g, '');
    if (!phonePattern.test(cleanedPhone)) {
      setPhoneError(t('phone_invalid', 'Please enter a valid Swiss phone number'));
      return false;
    }
    setPhoneError('');
    return true;
  };

  const handleNameBlur = () => {
    validateName(name);
  };

  const handleEmailBlur = () => {
    validateEmail(email);
  };

  const handlePhoneBlur = () => {
    validatePhone(phone);
  };

  const handleContinue = () => {
    // Validate all fields
    const isNameValid = validateName(name);
    const isEmailValid = validateEmail(email);
    const isPhoneValid = validatePhone(phone);

    if (!isNameValid || !isEmailValid || !isPhoneValid) {
      return;
    }

    // Prepare customer info
    const customerInfo = {
      name: name.trim(),
      email: email.trim(),
      phone: phone.trim(),
    };

    // Save to CheckoutContext
    setCustomerInfo(customerInfo);

    // Save to localStorage if user opted in
    if (saveInfo && typeof window !== 'undefined') {
      localStorage.setItem('rumi_saved_customer_info', JSON.stringify(customerInfo));
    }

    // Navigate to review page
    router.push('/checkout/review');
  };

  const handleBack = () => {
    router.push('/checkout/order-type');
  };

  return (
    <main className={styles.container}>
      <div className={styles.content}>
        <div className={styles.header}>
          <h1 className={styles.title}>{t('customer_information', 'Customer Information')}</h1>
          <p className={styles.subtitle}>
            {t('customer_info_desc', 'Please provide your contact information')}
          </p>
        </div>

        {/* Order Type Summary */}
        <div className={styles.orderTypeSummary}>
          <span className={styles.label}>{t('order_type', 'Order Type')}:</span>
          <span className={styles.value}>
            {checkoutState.orderType === 'DineIn' && t('order_type_dine_in', 'Dine In')}
            {checkoutState.orderType === 'Takeaway' && t('order_type_takeaway', 'Takeaway')}
            {checkoutState.orderType === 'Delivery' && t('order_type_delivery', 'Delivery')}
          </span>
          {checkoutState.orderType === 'DineIn' && checkoutState.tableNumber && (
            <span className={styles.detail}>
              {t('table', 'Table')} {checkoutState.tableNumber}
            </span>
          )}
          {checkoutState.orderType === 'Delivery' && checkoutState.deliveryAddress && (
            <span className={styles.detail}>
              {checkoutState.deliveryAddress.street}, {checkoutState.deliveryAddress.city}
            </span>
          )}
        </div>

        {/* Customer Info Form */}
        <form className={styles.form} onSubmit={(e) => { e.preventDefault(); handleContinue(); }}>
          {/* Name Field */}
          <div className={styles.inputGroup}>
            <label htmlFor="name" className={styles.label}>
              <User size={20} />
              {t('full_name', 'Full Name')}
              <span className={styles.required}>*</span>
            </label>
            <input
              type="text"
              id="name"
              value={name}
              onChange={(e) => {
                setName(e.target.value);
                if (nameError) setNameError('');
              }}
              onBlur={handleNameBlur}
              placeholder={t('enter_full_name', 'Enter your full name')}
              className={`${styles.input} ${nameError ? styles.inputError : ''}`}
              autoComplete="name"
            />
            {nameError && (
              <p className={styles.error}>
                <AlertCircle size={16} />
                {nameError}
              </p>
            )}
          </div>

          {/* Email Field */}
          <div className={styles.inputGroup}>
            <label htmlFor="email" className={styles.label}>
              <Mail size={20} />
              {t('email_address', 'Email Address')}
              <span className={styles.required}>*</span>
            </label>
            <input
              type="email"
              id="email"
              value={email}
              onChange={(e) => {
                setEmail(e.target.value);
                if (emailError) setEmailError('');
              }}
              onBlur={handleEmailBlur}
              placeholder={t('enter_email', 'Enter your email address')}
              className={`${styles.input} ${emailError ? styles.inputError : ''}`}
              autoComplete="email"
            />
            {emailError && (
              <p className={styles.error}>
                <AlertCircle size={16} />
                {emailError}
              </p>
            )}
          </div>

          {/* Phone Field */}
          <div className={styles.inputGroup}>
            <label htmlFor="phone" className={styles.label}>
              <Phone size={20} />
              {t('phone_number', 'Phone Number')}
              <span className={styles.required}>*</span>
            </label>
            <input
              type="tel"
              id="phone"
              value={phone}
              onChange={(e) => {
                setPhone(e.target.value);
                if (phoneError) setPhoneError('');
              }}
              onBlur={handlePhoneBlur}
              placeholder={t('enter_phone', '079 123 45 67')}
              className={`${styles.input} ${phoneError ? styles.inputError : ''}`}
              autoComplete="tel"
            />
            {phoneError && (
              <p className={styles.error}>
                <AlertCircle size={16} />
                {phoneError}
              </p>
            )}
            <p className={styles.hint}>
              {t('phone_hint', 'Swiss phone number (e.g., +41 79 123 45 67 or 079 123 45 67)')}
            </p>
          </div>

          {/* Save Info Checkbox */}
          <div className={styles.checkboxGroup}>
            <label className={styles.checkboxLabel}>
              <input
                type="checkbox"
                checked={saveInfo}
                onChange={(e) => setSaveInfo(e.target.checked)}
                className={styles.checkbox}
              />
              <span>{t('save_info_for_next_time', 'Save my information for next time')}</span>
            </label>
            <p className={styles.checkboxHint}>
              {t('save_info_hint', 'Your information will be stored locally on your device')}
            </p>
          </div>

          {/* Actions */}
          <div className={styles.actions}>
            <button
              type="button"
              onClick={handleBack}
              className={styles.backButton}
            >
              {t('back', 'Back')}
            </button>
            <button
              type="submit"
              className={styles.continueButton}
            >
              {t('continue_to_review', 'Continue to Review')}
              <ChevronRight size={20} />
            </button>
          </div>
        </form>
      </div>
    </main>
  );
}
