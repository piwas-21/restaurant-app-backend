"use client";

import React, { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useCheckout } from '@/contexts/CheckoutContext';
import { useCart } from '@/components/cart/CartContext';
import { useTableContext } from '@/contexts/TableContext';
import { OrderType } from '@/types/order';
import {
  Store,
  Utensils,
  Truck,
  ChevronRight,
  MapPin,
  Hash
} from 'lucide-react';
import styles from '../../styles/OrderTypePage.module.css';

export default function OrderTypePage() {
  const { t } = useTranslation();
  const router = useRouter();
  const { state: checkoutState, setOrderType, setTableNumber, setDeliveryAddress } = useCheckout();
  const { state: cartState } = useCart();
  const { tableContext, hasTableContext } = useTableContext();
  const [selectedType, setSelectedType] = useState<OrderType | null>(checkoutState.orderType);
  const [tableNum, setTableNum] = useState(checkoutState.tableNumber || '');
  const [tableError, setTableError] = useState('');

  // Delivery address fields
  const [street, setStreet] = useState(checkoutState.deliveryAddress?.street || '');
  const [city, setCity] = useState(checkoutState.deliveryAddress?.city || '');
  const [postalCode, setPostalCode] = useState(checkoutState.deliveryAddress?.postalCode || '');
  const [country, setCountry] = useState(checkoutState.deliveryAddress?.country || 'Switzerland');
  const [additionalInfo, setAdditionalInfo] = useState(checkoutState.deliveryAddress?.additionalInfo || '');
  const [addressError, setAddressError] = useState('');

  // Check for table context from QR code scan
  useEffect(() => {
    if (hasTableContext && tableContext.tableNumber) {
      // Auto-select dine-in and pre-fill table number
      setSelectedType(OrderType.DineIn);
      setTableNum(tableContext.tableNumber);
      setOrderType(OrderType.DineIn);
      setTableNumber(tableContext.tableNumber);
    }
  }, [hasTableContext, tableContext, setOrderType, setTableNumber]);

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

  const handleTypeSelect = (type: OrderType) => {
    setSelectedType(type);
    setTableError('');
    setAddressError('');
  };

  const validateTableNumber = (): boolean => {
    if (!tableNum.trim()) {
      setTableError(t('table_number_required', 'Please enter a table number'));
      return false;
    }
    const tableNumber = parseInt(tableNum);
    if (isNaN(tableNumber) || tableNumber < 1 || tableNumber > 100) {
      setTableError(t('table_number_invalid', 'Please enter a valid table number (1-100)'));
      return false;
    }
    setTableError('');
    return true;
  };

  const validateDeliveryAddress = (): boolean => {
    if (!street.trim()) {
      setAddressError(t('street_required', 'Street address is required'));
      return false;
    }
    if (!city.trim()) {
      setAddressError(t('city_required', 'City is required'));
      return false;
    }
    if (!postalCode.trim()) {
      setAddressError(t('postal_code_required', 'Postal code is required'));
      return false;
    }
    if (!/^\d{4}$/.test(postalCode.trim())) {
      setAddressError(t('postal_code_invalid', 'Please enter a valid Swiss postal code (4 digits)'));
      return false;
    }
    setAddressError('');
    return true;
  };

  const handleContinue = () => {
    if (!selectedType) {
      return;
    }

    // Validate based on order type
    if (selectedType === OrderType.DineIn) {
      if (!validateTableNumber()) {
        return;
      }
      setTableNumber(tableNum);
      setDeliveryAddress({ street: '', city: '', postalCode: '', country: '' });
    } else if (selectedType === OrderType.Delivery) {
      if (!validateDeliveryAddress()) {
        return;
      }
      setDeliveryAddress({
        street: street.trim(),
        city: city.trim(),
        postalCode: postalCode.trim(),
        country: country.trim(),
        additionalInfo: additionalInfo.trim(),
      });
      setTableNumber('');
    } else {
      // Takeaway - clear both
      setTableNumber('');
      setDeliveryAddress({ street: '', city: '', postalCode: '', country: '' });
    }

    // Save order type
    setOrderType(selectedType);

    // Navigate to customer info page
    router.push('/checkout/customer-info');
  };

  const orderTypes = [
    {
      type: OrderType.DineIn,
      icon: Utensils,
      title: t('order_type_dine_in', 'Dine In'),
      description: t('order_type_dine_in_desc', 'Enjoy your meal at our restaurant'),
    },
    {
      type: OrderType.Takeaway,
      icon: Store,
      title: t('order_type_takeaway', 'Takeaway'),
      description: t('order_type_takeaway_desc', 'Pick up your order'),
    },
    {
      type: OrderType.Delivery,
      icon: Truck,
      title: t('order_type_delivery', 'Delivery'),
      description: t('order_type_delivery_desc', 'We deliver to your address'),
    },
  ];

  return (
    <main className={styles.container}>
      <div className={styles.content}>
        <h1 className={styles.title}>{t('select_order_type', 'Select Order Type')}</h1>
        <p className={styles.subtitle}>
          {t('select_order_type_desc', 'How would you like to receive your order?')}
        </p>

        {/* Order Type Selection */}
        <div className={styles.orderTypes}>
          {orderTypes.map(({ type, icon: Icon, title, description }) => (
            <button
              key={type}
              className={`${styles.orderTypeCard} ${selectedType === type ? styles.selected : ''}`}
              onClick={() => handleTypeSelect(type)}
            >
              <Icon className={styles.orderTypeIcon} size={48} />
              <h2 className={styles.orderTypeTitle}>{title}</h2>
              <p className={styles.orderTypeDescription}>{description}</p>
            </button>
          ))}
        </div>

        {/* Dine-in Table Number Input */}
        {selectedType === OrderType.DineIn && (
          <div className={styles.detailsSection}>
            <div className={styles.inputGroup}>
              <label htmlFor="tableNumber" className={styles.label}>
                <Hash size={20} />
                {t('table_number', 'Table Number')}
                <span className={styles.required}>*</span>
              </label>
              <input
                type="number"
                id="tableNumber"
                value={tableNum}
                onChange={(e) => {
                  setTableNum(e.target.value);
                  setTableError('');
                }}
                placeholder={t('enter_table_number', 'Enter your table number')}
                className={`${styles.input} ${tableError ? styles.inputError : ''}`}
                min="1"
                max="100"
                readOnly={hasTableContext}
              />
              {tableError && <p className={styles.error}>{tableError}</p>}
              {hasTableContext && (
                <p className={styles.infoText}>
                  {t('table_from_qr', 'Table number set from QR code scan')}
                </p>
              )}
            </div>
          </div>
        )}

        {/* Delivery Address Form */}
        {selectedType === OrderType.Delivery && (
          <div className={styles.detailsSection}>
            <h3 className={styles.sectionTitle}>
              <MapPin size={20} />
              {t('delivery_address', 'Delivery Address')}
            </h3>

            <div className={styles.addressForm}>
              <div className={styles.inputGroup}>
                <label htmlFor="street" className={styles.label}>
                  {t('street_address', 'Street Address')}
                  <span className={styles.required}>*</span>
                </label>
                <input
                  type="text"
                  id="street"
                  value={street}
                  onChange={(e) => {
                    setStreet(e.target.value);
                    setAddressError('');
                  }}
                  placeholder={t('enter_street', 'Enter street address')}
                  className={`${styles.input} ${addressError && !street.trim() ? styles.inputError : ''}`}
                />
              </div>

              <div className={styles.inputRow}>
                <div className={styles.inputGroup}>
                  <label htmlFor="postalCode" className={styles.label}>
                    {t('postal_code', 'Postal Code')}
                    <span className={styles.required}>*</span>
                  </label>
                  <input
                    type="text"
                    id="postalCode"
                    value={postalCode}
                    onChange={(e) => {
                      setPostalCode(e.target.value);
                      setAddressError('');
                    }}
                    placeholder={t('enter_postal_code', '1234')}
                    className={`${styles.input} ${addressError && !postalCode.trim() ? styles.inputError : ''}`}
                    maxLength={4}
                  />
                </div>

                <div className={styles.inputGroup}>
                  <label htmlFor="city" className={styles.label}>
                    {t('city', 'City')}
                    <span className={styles.required}>*</span>
                  </label>
                  <input
                    type="text"
                    id="city"
                    value={city}
                    onChange={(e) => {
                      setCity(e.target.value);
                      setAddressError('');
                    }}
                    placeholder={t('enter_city', 'Enter city')}
                    className={`${styles.input} ${addressError && !city.trim() ? styles.inputError : ''}`}
                  />
                </div>
              </div>

              <div className={styles.inputGroup}>
                <label htmlFor="country" className={styles.label}>
                  {t('country', 'Country')}
                </label>
                <input
                  type="text"
                  id="country"
                  value={country}
                  onChange={(e) => setCountry(e.target.value)}
                  placeholder={t('enter_country', 'Switzerland')}
                  className={styles.input}
                />
              </div>

              <div className={styles.inputGroup}>
                <label htmlFor="additionalInfo" className={styles.label}>
                  {t('additional_info', 'Additional Information')}
                  <span className={styles.optional}> ({t('optional', 'optional')})</span>
                </label>
                <textarea
                  id="additionalInfo"
                  value={additionalInfo}
                  onChange={(e) => setAdditionalInfo(e.target.value)}
                  placeholder={t('additional_info_placeholder', 'Floor, apartment number, building, etc.')}
                  className={styles.textarea}
                  rows={3}
                />
              </div>

              {addressError && <p className={styles.error}>{addressError}</p>}
            </div>
          </div>
        )}

        {/* Continue Button */}
        <div className={styles.actions}>
          <button
            onClick={() => router.back()}
            className={styles.backButton}
          >
            {t('back', 'Back')}
          </button>
          <button
            onClick={handleContinue}
            disabled={!selectedType}
            className={styles.continueButton}
          >
            {t('continue', 'Continue')}
            <ChevronRight size={20} />
          </button>
        </div>
      </div>
    </main>
  );
}
