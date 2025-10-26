"use client";

import React, { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useCheckout } from '@/contexts/CheckoutContext';
import { useCart } from '@/components/cart/CartContext';
import { useSession } from '@/hooks/useSession';
import { createOrder } from '@/services/orderService';
import FidelityPointsCheckout from '@/components/checkout/FidelityPointsCheckout';
import {
  PaymentMethod,
  CreateOrderCommand,
  CreateOrderItemDto,
  CreateOrderDeliveryAddressDto,
  OrderType as OrderTypeEnum
} from '@/types/order';
import { useSnackbar } from 'notistack';
import {
  ShoppingBag,
  User,
  MapPin,
  CreditCard,
  Wallet,
  Smartphone,
  Banknote,
  Building2,
  ArrowRight,
  AlertCircle,
  Loader2,
  CheckCircle,
  Edit,
  Info,
} from 'lucide-react';
import Image from 'next/image';
import styles from '../../styles/ReviewPage.module.css';

export default function ReviewPage() {
  const { t } = useTranslation();
  const router = useRouter();
  const { enqueueSnackbar } = useSnackbar();
  const { state: checkoutState, clearCheckout } = useCheckout();
  const { state: cartState, clearCart } = useCart();
  const { sessionId } = useSession();

  // Payment method state
  const [selectedPaymentMethod, setSelectedPaymentMethod] = useState<PaymentMethod>(PaymentMethod.Cash);

  // Fidelity points redemption state
  const [redeemedPoints, setRedeemedPoints] = useState(0);
  const [pointsDiscount, setPointsDiscount] = useState(0);

  // Submission state
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState('');

  // Handler for points redemption
  const handlePointsRedemption = (points: number, discountAmount: number) => {
    setRedeemedPoints(points);
    setPointsDiscount(discountAmount);
  };

  // Check prerequisites
  useEffect(() => {
    if (cartState.items.length === 0) {
      router.push('/cart');
    } else if (!checkoutState.orderType) {
      router.push('/checkout/order-type');
    } else if (!checkoutState.customerInfo) {
      router.push('/checkout/customer-info');
    }
  }, [cartState.items.length, checkoutState.orderType, checkoutState.customerInfo, router]);

  if (cartState.items.length === 0 || !checkoutState.orderType || !checkoutState.customerInfo) {
    return (
      <main className={styles.container}>
        <div className={styles.loadingState}>
          <Loader2 size={48} className={styles.spinner} />
          <p>{t('loading', 'Loading...')}</p>
        </div>
      </main>
    );
  }

  const formatPrice = (price: number) => {
    return new Intl.NumberFormat('de-CH', {
      style: 'currency',
      currency: 'CHF',
    }).format(price);
  };

  const paymentMethods = [
    {
      value: PaymentMethod.Cash,
      label: t('payment_cash', 'Cash'),
      icon: Banknote,
      description: t('payment_cash_desc', 'Pay on cashier'),
      disabled: false
    },
    {
      value: PaymentMethod.CreditCard,
      label: t('payment_credit_card', 'Credit Card'),
      icon: CreditCard,
      description: t('payment_credit_card_desc', 'Visa, Mastercard, Amex'),
      disabled: true
    },
    {
      value: PaymentMethod.DebitCard,
      label: t('payment_debit_card', 'Debit Card'),
      icon: Wallet,
      description: t('payment_debit_card_desc', 'EC/Maestro card'),
      disabled: true
    },
    {
      value: PaymentMethod.MobilePayment,
      label: t('payment_mobile', 'Mobile Payment'),
      icon: Smartphone,
      description: t('payment_mobile_desc', 'TWINT, Apple Pay, Google Pay'),
      disabled: true
    },
    {
      value: PaymentMethod.OnlinePayment,
      label: t('payment_online', 'Online Payment'),
      icon: CreditCard,
      description: t('payment_online_desc', 'Pay securely online'),
      disabled: true
    },
    {
      value: PaymentMethod.BankTransfer,
      label: t('payment_bank_transfer', 'Bank Transfer'),
      icon: Building2,
      description: t('payment_bank_transfer_desc', 'Transfer to our account'),
      disabled: true
    },
  ];  const handlePlaceOrder = async () => {
    setIsSubmitting(true);
    setSubmitError('');

    try {
      // Convert basket items to order items
      const orderItems: CreateOrderItemDto[] = cartState.items.map(item => ({
        productId: item.productId || '',
        productVariationId: item.productVariationId,
        menuId: item.menuId,
        quantity: item.quantity,
        unitPrice: item.unitPrice,
        specialInstructions: item.specialInstructions,
      }));

      // Prepare delivery address if delivery order
      let deliveryAddress: CreateOrderDeliveryAddressDto | undefined;
      if (checkoutState.orderType === 'Delivery' && checkoutState.deliveryAddress) {
        deliveryAddress = {
          street: checkoutState.deliveryAddress.street,
          city: checkoutState.deliveryAddress.city,
          postalCode: checkoutState.deliveryAddress.postalCode,
          country: checkoutState.deliveryAddress.country,
          additionalInfo: checkoutState.deliveryAddress.additionalInfo,
        };
      }

      // Build order command
      const orderCommand: CreateOrderCommand = {
        sessionId: sessionId || undefined,
        customerName: checkoutState.customerInfo?.name,
        customerEmail: checkoutState.customerInfo?.email,
        customerPhone: checkoutState.customerInfo?.phone,
        type: checkoutState.orderType as OrderTypeEnum,
        tableNumber: checkoutState.orderType === 'DineIn' && checkoutState.tableNumber
          ? parseInt(checkoutState.tableNumber, 10)
          : undefined,
        notes: checkoutState.specialInstructions || undefined,
        deliveryAddress,
        items: orderItems,
        payments: [{
          paymentMethod: selectedPaymentMethod,
          amount: cartState.basket?.total || 0,
        }],
        promoCode: cartState.basket?.promoCode || undefined,
      };

      // Submit order
      const createdOrder = await createOrder(orderCommand);

      // Clear cart and checkout state
      await clearCart();
      clearCheckout();

      // Show success message
      enqueueSnackbar(t('order_placed_success', 'Order placed successfully!'), {
        variant: 'success',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });

      // Navigate to confirmation page
      router.push(`/checkout/confirmation?orderId=${createdOrder.id}&orderNumber=${createdOrder.orderNumber}`);

    } catch (error) {
      // eslint-disable-next-line no-console
      console.error('Error placing order:', error);

      const errorMessage = error instanceof Error
        ? error.message
        : t('order_failed', 'Failed to place order. Please try again.');

      setSubmitError(errorMessage);

      enqueueSnackbar(errorMessage, {
        variant: 'error',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <main className={styles.container}>
      <div className={styles.content}>
        {/* Header */}
        <div className={styles.header}>
          <h1 className={styles.title}>{t('review_order', 'Review Your Order')}</h1>
          <p className={styles.subtitle}>
            {t('review_order_desc', 'Please review your order details and choose a payment method')}
          </p>
        </div>

        <div className={styles.gridLayout}>
          {/* Left Column - Order Details */}
          <div className={styles.leftColumn}>
            {/* Order Type Section */}
            <section className={styles.section}>
              <div className={styles.sectionHeader}>
                <h2 className={styles.sectionTitle}>
                  <ShoppingBag size={20} />
                  {t('order_details', 'Order Details')}
                </h2>
                <button
                  onClick={() => router.push('/checkout/order-type')}
                  className={styles.editButton}
                >
                  <Edit size={16} />
                  {t('edit', 'Edit')}
                </button>
              </div>
              <div className={styles.infoCard}>
                <div className={styles.infoRow}>
                  <span className={styles.infoLabel}>{t('order_type', 'Order Type')}:</span>
                  <span className={styles.infoValue}>
                    {checkoutState.orderType === 'DineIn' && t('order_type_dine_in', 'Dine In')}
                    {checkoutState.orderType === 'Takeaway' && t('order_type_takeaway', 'Takeaway')}
                    {checkoutState.orderType === 'Delivery' && t('order_type_delivery', 'Delivery')}
                  </span>
                </div>
                {checkoutState.orderType === 'DineIn' && checkoutState.tableNumber && (
                  <div className={styles.infoRow}>
                    <span className={styles.infoLabel}>{t('table_number', 'Table Number')}:</span>
                    <span className={styles.infoValue}>{checkoutState.tableNumber}</span>
                  </div>
                )}
                {checkoutState.orderType === 'Delivery' && checkoutState.deliveryAddress && (
                  <div className={styles.deliveryAddress}>
                    <MapPin size={18} className={styles.addressIcon} />
                    <div>
                      <p>{checkoutState.deliveryAddress.street}</p>
                      <p>{checkoutState.deliveryAddress.postalCode} {checkoutState.deliveryAddress.city}</p>
                      <p>{checkoutState.deliveryAddress.country}</p>
                      {checkoutState.deliveryAddress.additionalInfo && (
                        <p className={styles.additionalInfo}>{checkoutState.deliveryAddress.additionalInfo}</p>
                      )}
                    </div>
                  </div>
                )}
              </div>
            </section>

            {/* Customer Info Section */}
            <section className={styles.section}>
              <div className={styles.sectionHeader}>
                <h2 className={styles.sectionTitle}>
                  <User size={20} />
                  {t('customer_information', 'Customer Information')}
                </h2>
                <button
                  onClick={() => router.push('/checkout/customer-info')}
                  className={styles.editButton}
                >
                  <Edit size={16} />
                  {t('edit', 'Edit')}
                </button>
              </div>
              <div className={styles.infoCard}>
                <div className={styles.infoRow}>
                  <span className={styles.infoLabel}>{t('name', 'Name')}:</span>
                  <span className={styles.infoValue}>{checkoutState.customerInfo.name}</span>
                </div>
                <div className={styles.infoRow}>
                  <span className={styles.infoLabel}>{t('email', 'Email')}:</span>
                  <span className={styles.infoValue}>{checkoutState.customerInfo.email}</span>
                </div>
                <div className={styles.infoRow}>
                  <span className={styles.infoLabel}>{t('phone', 'Phone')}:</span>
                  <span className={styles.infoValue}>{checkoutState.customerInfo.phone}</span>
                </div>
              </div>
            </section>

            {/* Cart Items Section */}
            <section className={styles.section}>
              <div className={styles.sectionHeader}>
                <h2 className={styles.sectionTitle}>
                  <ShoppingBag size={20} />
                  {t('order_items', 'Order Items')} ({cartState.items.length})
                </h2>
                <button
                  onClick={() => router.push('/cart')}
                  className={styles.editButton}
                >
                  <Edit size={16} />
                  {t('edit', 'Edit')}
                </button>
              </div>
              <div className={styles.itemsList}>
                {cartState.items.map((item) => (
                  <div key={item.id} className={styles.cartItem}>
                    {item.productImageUrl && (
                      <div className={styles.itemImage}>
                        <Image
                          src={item.productImageUrl}
                          alt={item.productName || ''}
                          width={60}
                          height={60}
                          style={{ objectFit: 'cover' }}
                        />
                      </div>
                    )}
                    <div className={styles.itemDetails}>
                      <h3 className={styles.itemName}>{item.productName}</h3>
                      {item.variationName && (
                        <p className={styles.itemVariation}>{item.variationName}</p>
                      )}
                      {item.specialInstructions && (
                        <p className={styles.itemInstructions}>
                          <i>{item.specialInstructions}</i>
                        </p>
                      )}
                      <p className={styles.itemQuantity}>
                        {t('quantity', 'Qty')}: {item.quantity} × {formatPrice(item.unitPrice)}
                      </p>
                    </div>
                    <div className={styles.itemPrice}>
                      {formatPrice(item.itemTotal)}
                    </div>
                  </div>
                ))}
              </div>
            </section>

            {/* Payment Method Section */}
            <section className={styles.section}>
              <div className={styles.sectionHeader}>
                <h2 className={styles.sectionTitle}>
                  <CreditCard size={20} />
                  {t('payment_method', 'Payment Method')}
                </h2>
              </div>

              {/* Info message about payment methods under development */}
              <div className={styles.infoMessage}>
                <Info size={18} />
                <p>
                  {t('payment_methods_info', 'Currently, only cash payment is available. Other payment methods are coming soon!')}
                </p>
              </div>

              <div className={styles.paymentMethods}>
                {paymentMethods.map((method) => {
                  const Icon = method.icon;
                  const isDisabled = method.disabled;

                  return (
                    <label
                      key={method.value}
                      className={`${styles.paymentMethod} ${
                        selectedPaymentMethod === method.value ? styles.selected : ''
                      } ${isDisabled ? styles.disabled : ''}`}
                    >
                      <input
                        type="radio"
                        name="paymentMethod"
                        value={method.value}
                        checked={selectedPaymentMethod === method.value}
                        onChange={() => !isDisabled && setSelectedPaymentMethod(method.value)}
                        className={styles.paymentRadio}
                        disabled={isDisabled}
                      />
                      <div className={styles.paymentIcon}>
                        <Icon size={24} />
                      </div>
                      <div className={styles.paymentInfo}>
                        <span className={styles.paymentLabel}>
                          {method.label}
                          {isDisabled && <span className={styles.comingSoon}> ({t('coming_soon', 'Coming Soon')})</span>}
                        </span>
                        <span className={styles.paymentDescription}>{method.description}</span>
                      </div>
                      {selectedPaymentMethod === method.value && !isDisabled && (
                        <CheckCircle size={20} className={styles.checkmark} />
                      )}
                    </label>
                  );
                })}
              </div>
            </section>

            {/* Special Instructions */}
            {checkoutState.specialInstructions && (
              <section className={styles.section}>
                <h2 className={styles.sectionTitle}>{t('special_instructions', 'Special Instructions')}</h2>
                <div className={styles.infoCard}>
                  <p>{checkoutState.specialInstructions}</p>
                </div>
              </section>
            )}
          </div>

          {/* Right Column - Order Summary */}
          <div className={styles.rightColumn}>
            {/* Fidelity Points Section */}
            <FidelityPointsCheckout
              orderSubtotal={cartState.basket?.subTotal || 0}
              onPointsRedemption={handlePointsRedemption}
            />

            <div className={styles.summaryCard}>
              <h2 className={styles.summaryTitle}>{t('order_summary', 'Order Summary')}</h2>

              <div className={styles.summaryRows}>
                <div className={styles.summaryRow}>
                  <span>{t('subtotal', 'Subtotal')}</span>
                  <span>{formatPrice(cartState.basket?.subTotal || 0)}</span>
                </div>

                {(cartState.basket?.discount ?? 0) > 0 && (
                  <div className={`${styles.summaryRow} ${styles.discount}`}>
                    <span>{t('discount', 'Discount')}</span>
                    <span>-{formatPrice(cartState.basket?.discount || 0)}</span>
                  </div>
                )}

                {pointsDiscount > 0 && (
                  <div className={`${styles.summaryRow} ${styles.discount}`}>
                    <span>{t('points_discount', 'Points Discount')} ({redeemedPoints} pts)</span>
                    <span>-{formatPrice(pointsDiscount)}</span>
                  </div>
                )}

                {(cartState.basket?.deliveryFee ?? 0) > 0 && (
                  <div className={styles.summaryRow}>
                    <span>{t('delivery_fee', 'Delivery Fee')}</span>
                    <span>{formatPrice(cartState.basket?.deliveryFee || 0)}</span>
                  </div>
                )}

                <div className={styles.summaryRow}>
                  <span>{t('tax', 'Tax')}</span>
                  <span>{formatPrice(cartState.basket?.tax || 0)}</span>
                </div>
              </div>

              <div className={styles.summaryTotal}>
                <span>{t('total', 'Total')}</span>
                <span className={styles.totalAmount}>
                  {formatPrice((cartState.basket?.total || 0) - pointsDiscount)}
                </span>
              </div>

              {submitError && (
                <div className={styles.errorAlert}>
                  <AlertCircle size={20} />
                  <p>{submitError}</p>
                </div>
              )}

              <button
                onClick={handlePlaceOrder}
                disabled={isSubmitting}
                className={styles.placeOrderButton}
              >
                {isSubmitting ? (
                  <>
                    <Loader2 size={20} className={styles.spinner} />
                    {t('placing_order', 'Placing Order...')}
                  </>
                ) : (
                  <>
                    {t('place_order', 'Place Order')}
                    <ArrowRight size={20} />
                  </>
                )}
              </button>

              <button
                onClick={() => router.push('/checkout/customer-info')}
                disabled={isSubmitting}
                className={styles.backButton}
              >
                {t('back', 'Back')}
              </button>
            </div>
          </div>
        </div>
      </div>
    </main>
  );
}
