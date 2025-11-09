"use client";

import React, { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useCheckout } from '@/contexts/CheckoutContext';
import { useCart } from '@/components/cart/CartContext';
import { useTableContext } from '@/contexts/TableContext';
import { OrderType } from '@/types/order';
import { getCurrentUser } from '@/services/userService';
import {
  User,
  Mail,
  Phone,
  ChevronRight,
  AlertCircle,
  Utensils,
  Star,
  LogIn
} from 'lucide-react';
import styles from '../../styles/CustomerInfoPage.module.css';

export default function CustomerInfoPage() {
  const { t } = useTranslation();
  const router = useRouter();
  const { state: checkoutState, setCustomerInfo, setOrderType, setTableNumber } = useCheckout();
  const { state: cartState } = useCart();
  const { tableContext, hasTableContext } = useTableContext();

  // Auto-set dine-in order type if coming from QR scan
  useEffect(() => {
    if (hasTableContext && tableContext.tableNumber && !checkoutState.orderType) {
      setOrderType(OrderType.DineIn);
      setTableNumber(tableContext.tableNumber);
    }
  }, [hasTableContext, tableContext, checkoutState.orderType, setOrderType, setTableNumber]);

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

  // User auth state
  const [isLoggedIn, setIsLoggedIn] = useState(false);
  const [isLoadingUser, setIsLoadingUser] = useState(true);

  // Load user data and saved customer info on mount
  useEffect(() => {
    const loadUserData = async () => {
      setIsLoadingUser(true);
      try {
        // Check if user has an auth token (means they're logged in)
        if (typeof window !== 'undefined') {
          const authToken = localStorage.getItem('auth_token');

          if (authToken && !checkoutState.customerInfo) {
            // User is logged in, try to fetch their data
            try {
              const user = await getCurrentUser();
              if (user) {
                // Prefill with user data
                setName(user.firstName && user.lastName ? `${user.firstName} ${user.lastName}` : user.email);
                setEmail(user.email || '');
                setPhone(user.phoneNumber || '');
                setIsLoggedIn(true);
                return;
              }
            } catch (err) {
              // Token might be expired, continue to load from localStorage
              console.error('Failed to fetch user profile:', err);
            }
          }
        }

        // User is not logged in or fetch failed - try to load from localStorage
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
        setIsLoggedIn(false);
      } finally {
        setIsLoadingUser(false);
      }
    };

    loadUserData();
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

  // Check if order type is selected (skip this check if coming from QR scan)
  if (!checkoutState.orderType && !hasTableContext) {
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
    // Phone is now optional
    if (!value.trim()) {
      setPhoneError('');
      return true;
    }
    // Accept any phone number format with at least 5 digits
    const phonePattern = /^[+\s\-()0-9]{5,}$/;
    if (!phonePattern.test(value.trim())) {
      setPhoneError(t('phone_invalid', 'Please enter a valid phone number'));
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
    // If user came from QR scan, go back to cart
    if (hasTableContext) {
      router.push('/cart');
    } else {
      router.push('/checkout/order-type');
    }
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

        {/* Table Banner for QR Scan Users */}
        {hasTableContext && tableContext.tableNumber && (
          <div className={styles.tableBanner}>
            <Utensils size={24} className={styles.tableBannerIcon} />
            <span className={styles.tableBannerText}>
              {t('ordering_for_table', 'Ordering for Table {{number}}', { number: tableContext.tableNumber })}
            </span>
          </div>
        )}

        {/* Order Type Summary for non-QR users */}
        {!hasTableContext && checkoutState.orderType && (
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
        )}

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
              <span className={styles.optional}>{t('optional', '(Optional)')}</span>
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
              placeholder={t('enter_phone', '+1 555 123 4567 or any phone number')}
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
              {t('phone_hint_optional', 'You can enter a phone number in any format (e.g., +1 555 123 4567 or 555-123-4567)')}
            </p>
          </div>

          {/* Save Info Checkbox - Only show if not logged in */}
          {!isLoggedIn && (
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
          )}

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

        {/* Registration Encouragement Section - Show if not logged in */}
        {!isLoadingUser && !isLoggedIn && (
          <div className={styles.registrationSection}>
            <div className={styles.registrationCard}>
              <div className={styles.registrationHeader}>
                <Star size={24} className={styles.registrationIcon} />
                <h2 className={styles.registrationTitle}>
                  {t('dont_have_account', 'Don\'t have an account yet?')}
                </h2>
              </div>
              <p className={styles.registrationDescription}>
                {t('register_benefits', 'Create a RUMI account to unlock exclusive benefits')}
              </p>
              <div className={styles.benefitsList}>
                <div className={styles.benefitItem}>
                  <Star size={18} className={styles.benefitIcon} />
                  <span>{t('benefit_fidelity', 'Earn Fidelity Points on every order')}</span>
                </div>
                <div className={styles.benefitItem}>
                  <Star size={18} className={styles.benefitIcon} />
                  <span>{t('benefit_rewards', 'Redeem points for discounts')}</span>
                </div>
                <div className={styles.benefitItem}>
                  <Star size={18} className={styles.benefitIcon} />
                  <span>{t('benefit_tracking', 'Track your order history')}</span>
                </div>
                <div className={styles.benefitItem}>
                  <Star size={18} className={styles.benefitIcon} />
                  <span>{t('benefit_reservations', 'Manage your reservations easily')}</span>
                </div>
              </div>
              <button
                type="button"
                onClick={() => router.push('/auth/register')}
                className={styles.registerButton}
              >
                <LogIn size={18} />
                {t('register_now', 'Register Now')}
              </button>
              <p className={styles.registrationFooter}>
                {t('already_member', 'Already a member?')}
                <button
                  type="button"
                  onClick={() => router.push('/auth/login')}
                  className={styles.loginLink}
                >
                  {t('login_here', 'Login here')}
                </button>
              </p>
            </div>
          </div>
        )}
      </div>
    </main>
  );
}
