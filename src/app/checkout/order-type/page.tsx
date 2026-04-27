'use client';

import React, { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useCheckout } from '@/contexts/CheckoutContext';
import { useCart } from '@/components/cart/CartContext';
import { useTableContext } from '@/contexts/TableContext';
import { OrderType } from '@/types/order';
import { Store, Utensils, Truck, ChevronRight, MapPin } from 'lucide-react';
import TableSelector from '@/components/checkout/TableSelector';
import { getCurrentUser } from '@/services/userService';
import { getMyAddresses, createAddress, AddressDto } from '@/services/addressService';
import { orderTypeConfigurationService } from '@/services/orderTypeConfigurationService';
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

  // Saved addresses state
  const [isLoggedIn, setIsLoggedIn] = useState(false);
  const [savedAddresses, setSavedAddresses] = useState<AddressDto[]>([]);
  const [selectedAddressId, setSelectedAddressId] = useState<string | null>(null);
  const [showNewAddressForm, setShowNewAddressForm] = useState(true);
  const [saveThisAddress, setSaveThisAddress] = useState(false);
  const [loadingAddresses, setLoadingAddresses] = useState(false);
  const [savingAddress, setSavingAddress] = useState(false);

  // Enabled order types state
  const [enabledOrderTypes, setEnabledOrderTypes] = useState<OrderType[]>([]);
  const [loadingOrderTypes, setLoadingOrderTypes] = useState(true);

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

  // Fetch enabled order types on mount
  useEffect(() => {
    const fetchEnabledOrderTypes = async () => {
      try {
        setLoadingOrderTypes(true);
        const enabled = await orderTypeConfigurationService.getEnabled();
        setEnabledOrderTypes(enabled);
      } catch (error) {
        console.error('Error fetching enabled order types:', error);
        // Fallback to all order types if fetch fails
        setEnabledOrderTypes([OrderType.DineIn, OrderType.Takeaway, OrderType.Delivery]);
      } finally {
        setLoadingOrderTypes(false);
      }
    };

    fetchEnabledOrderTypes();
  }, []);

  // Fetch saved addresses when delivery is selected and user is logged in
  useEffect(() => {
    const fetchAddresses = async () => {
      try {
        const user = await getCurrentUser();
        if (user) {
          setIsLoggedIn(true);
          setLoadingAddresses(true);
          const addresses = await getMyAddresses();
          setSavedAddresses(addresses);
          setShowNewAddressForm(addresses.length === 0);
        }
      } catch {
        // User is not logged in or error fetching addresses
        // Non-authenticated users can still proceed with checkout by entering address manually
        // Don't log the error to avoid console noise for expected auth failures
        setIsLoggedIn(false);
        setSavedAddresses([]);
        setShowNewAddressForm(true);
      } finally {
        setLoadingAddresses(false);
      }
    };

    if (selectedType === OrderType.Delivery) {
      fetchAddresses();
    }
  }, [selectedType]);

  // Check if cart is empty
  if (cartState.items.length === 0) {
    return (
      <main className={styles.container}>
        <div className={styles.emptyState}>
          <h1>{t('checkout_title', 'Checkout')}</h1>
          <p>{t('cart_empty_message', 'Your cart is empty')}</p>
          <button onClick={() => router.push('/menu')} className={styles.browseButton}>
            {t('cart_browse_menu_button', 'Browse Menu')}
          </button>
        </div>
      </main>
    );
  }

  const handleTypeSelect = (type: OrderType) => {
    setSelectedType(type);
    setAddressError('');
  };

  const handleSelectSavedAddress = (address: AddressDto) => {
    setSelectedAddressId(address.id);
    setStreet(address.addressLine1);
    setCity(address.city);
    setPostalCode(address.postalCode);
    setCountry(address.country || 'Switzerland');
    setAdditionalInfo(address.deliveryInstructions || '');
    setShowNewAddressForm(false);
  };

  const handleTableSelect = (tableNumber: string) => {
    setTableNum(tableNumber);
  };

  const validateTableSelection = (): boolean => {
    if (!tableNum.trim()) {
      setTableError(t('table_not_selected', 'Please select a table'));
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
    // Allow various postal code formats: 4 digits (CH), alphanumeric (NL, DE, etc.)
    if (!/^[A-Z0-9\s\-]{2,10}$/i.test(postalCode.trim())) {
      setAddressError(t('postal_code_invalid', 'Please enter a valid postal code'));
      return false;
    }
    setAddressError('');
    return true;
  };

  const handleContinue = async () => {
    if (!selectedType) {
      return;
    }

    // Validate based on order type
    if (selectedType === OrderType.DineIn) {
      if (!validateTableSelection()) {
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

      // Save address if user checked the checkbox and is logged in
      if (saveThisAddress && isLoggedIn) {
        setSavingAddress(true);
        try {
          await createAddress({
            label: `${city.trim()}, ${postalCode.trim()}`,
            addressLine1: street.trim(),
            city: city.trim(),
            postalCode: postalCode.trim(),
            country: country.trim(),
            deliveryInstructions: additionalInfo.trim(),
          });
        } catch {
          setSavingAddress(false);
          setAddressError(t('failed_to_save_address', 'Failed to save address. Please try again.'));
          return;
        }
        setSavingAddress(false);
      }
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

  // Filter order types to show only enabled ones
  const availableOrderTypes = orderTypes.filter((ot) => enabledOrderTypes.includes(ot.type));

  // Show loading state while fetching order types
  if (loadingOrderTypes) {
    return (
      <main className={styles.container}>
        <div className={styles.emptyState}>
          <h1>{t('checkout_title', 'Checkout')}</h1>
          <p>{t('common.loading', 'Loading...')}</p>
        </div>
      </main>
    );
  }

  // Show error if no order types are available
  if (availableOrderTypes.length === 0) {
    return (
      <main className={styles.container}>
        <div className={styles.emptyState}>
          <h1>{t('checkout_title', 'Checkout')}</h1>
          <p>
            {t('no_order_types_available', 'No order types are currently available. Please contact the restaurant.')}
          </p>
          <button onClick={() => router.push('/menu')} className={styles.browseButton}>
            {t('cart_browse_menu_button', 'Browse Menu')}
          </button>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.container}>
      <div className={styles.content}>
        <h1 className={styles.title}>{t('select_order_type', 'Select Order Type')}</h1>
        <p className={styles.subtitle}>{t('select_order_type_desc', 'How would you like to receive your order?')}</p>

        {/* Order Type Selection */}
        <div className={styles.orderTypes}>
          {availableOrderTypes.map(({ type, icon: Icon, title, description }) => (
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

        {/* Dine-in Table Selector */}
        {selectedType === OrderType.DineIn && (
          <div className={styles.detailsSection}>
            <h3 className={styles.sectionTitle}>
              <Utensils size={20} />
              {t('select_your_table', 'Select your Table')}
            </h3>

            <TableSelector selectedTable={tableNum} onTableSelect={handleTableSelect} disabled={hasTableContext} />

            {tableError && <p className={styles.error}>{tableError}</p>}
            {hasTableContext && (
              <p className={styles.infoText}>{t('table_from_qr', 'Table number set from QR code scan')}</p>
            )}
          </div>
        )}

        {/* Delivery Address Form */}
        {selectedType === OrderType.Delivery && (
          <div className={styles.detailsSection}>
            <h3 className={styles.sectionTitle}>
              <MapPin size={20} />
              {t('delivery_address', 'Delivery Address')}
            </h3>

            {/* Saved Addresses Section (for logged-in users) */}
            {isLoggedIn && !loadingAddresses && savedAddresses.length > 0 && (
              <div className={styles.savedAddressesSection}>
                <h4 className={styles.savedAddressesTitle}>{t('saved_addresses', 'Your Saved Addresses')}</h4>
                <div className={styles.savedAddressesList}>
                  {savedAddresses.map((address) => (
                    <button
                      key={address.id}
                      className={`${styles.savedAddressCard} ${
                        selectedAddressId === address.id ? styles.selected : ''
                      }`}
                      onClick={() => handleSelectSavedAddress(address)}
                      type="button"
                    >
                      <div className={styles.addressContent}>
                        <p className={styles.addressLabel}>{address.label}</p>
                        <p className={styles.addressDetails}>
                          {address.addressLine1}, {address.postalCode} {address.city}
                        </p>
                      </div>
                    </button>
                  ))}
                </div>
                {!showNewAddressForm && (
                  <button
                    className={styles.addNewAddressBtn}
                    onClick={() => {
                      setShowNewAddressForm(true);
                      setSelectedAddressId(null);
                      setStreet('');
                      setCity('');
                      setPostalCode('');
                      setCountry('Switzerland');
                      setAdditionalInfo('');
                    }}
                    type="button"
                  >
                    {t('add_new_address', 'Add New Address')}
                  </button>
                )}
              </div>
            )}

            {/* Address Form */}
            {showNewAddressForm && (
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

                {/* Save Address Checkbox - only for logged-in users */}
                {isLoggedIn && (
                  <div className={styles.checkboxGroup}>
                    <label className={styles.checkboxLabel}>
                      <input
                        type="checkbox"
                        checked={saveThisAddress}
                        onChange={(e) => setSaveThisAddress(e.target.checked)}
                        className={styles.checkbox}
                      />
                      <span>{t('save_this_address', 'Save this address for future orders')}</span>
                    </label>
                  </div>
                )}
              </div>
            )}
          </div>
        )}

        {/* Continue Button */}
        <div className={styles.actions}>
          <button onClick={() => router.back()} className={styles.backButton} disabled={savingAddress}>
            {t('back', 'Back')}
          </button>
          <button onClick={handleContinue} disabled={!selectedType || savingAddress} className={styles.continueButton}>
            {savingAddress ? t('saving', 'Saving...') : t('continue', 'Continue')}
            {!savingAddress && <ChevronRight size={20} />}
          </button>
        </div>
      </div>
    </main>
  );
}
