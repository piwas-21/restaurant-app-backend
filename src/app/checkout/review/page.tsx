"use client";

import React, { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useCheckout } from '@/contexts/CheckoutContext';
import { useCart } from '@/components/cart/CartContext';
import { useSession } from '@/hooks/useSession';
import { createOrder } from '@/services/orderService';
import { sendOrderConfirmationEmails } from '@/services/emailService';
import FidelityPointsCheckout from '@/components/checkout/FidelityPointsCheckout';
import OrderTypeSection from '@/components/checkout/OrderTypeSection';
import CustomerInfoSection from '@/components/checkout/CustomerInfoSection';
import OrderItemsList from '@/components/checkout/OrderItemsList';
import PaymentMethodSelector from '@/components/checkout/PaymentMethodSelector';
import OrderSummaryCard from '@/components/checkout/OrderSummaryCard';
import OrderConfirmationModal from '@/components/checkout/OrderConfirmationModal';
import { formatPriceWithRounding, hasActiveDiscount } from '@/utils/priceRounding';
import { adminTaxConfigurationService } from '@/services/adminTaxConfigurationService';
import { getTranslatedOrderError } from '@/utils/orderErrorHandler';
import type { TaxConfiguration } from '@/services/adminTaxConfigurationService';
import {
  PaymentMethod,
  CreateOrderCommand,
  CreateOrderItemDto,
  CreateOrderDeliveryAddressDto,
  OrderType as OrderTypeEnum
} from '@/types/order';
import { useSnackbar } from 'notistack';
import { Loader2 } from 'lucide-react';
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

  // Tax configuration state
  const [taxConfig, setTaxConfig] = useState<TaxConfiguration | null>(null);
  const [taxAmount, setTaxAmount] = useState(0);
  const [pointsDiscount, setPointsDiscount] = useState(0);

  // Submission state
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState('');

  // Order confirmation modal state
  const [showConfirmationModal, setShowConfirmationModal] = useState(false);
  const [confirmedOrder, setConfirmedOrder] = useState<{ id: string; orderNumber: string; customerEmail: string } | null>(null);

  // Check if user is logged in based on session
  const [isLoggedIn, setIsLoggedIn] = useState(false);
  useEffect(() => {
    // Check if user has an active session
    setIsLoggedIn(!!sessionId);
  }, [sessionId]);

  // Auto-show modal when order is confirmed
  useEffect(() => {
    if (confirmedOrder) {
      // eslint-disable-next-line no-console
      console.log('Order confirmed, showing modal:', confirmedOrder);
      setShowConfirmationModal(true);
    }
  }, [confirmedOrder]);

  // Debug modal state
  useEffect(() => {
    // eslint-disable-next-line no-console
    console.log('Modal state changed:', { showConfirmationModal, confirmedOrder, isLoggedIn });
  }, [showConfirmationModal, confirmedOrder, isLoggedIn]);

  // Handler for points redemption
  const handlePointsRedemption = (points: number, discountAmount: number) => {
    setRedeemedPoints(points);
    setPointsDiscount(discountAmount);
  };

  // Handler for closing confirmation modal
  const handleCloseConfirmationModal = () => {
    setShowConfirmationModal(false);
    if (confirmedOrder) {
      // Navigate to confirmation page
      router.push(`/checkout/confirmation?orderId=${confirmedOrder.id}&orderNumber=${confirmedOrder.orderNumber}`);
    }
  };

  // Fetch tax configuration based on order type
  useEffect(() => {
    const fetchTaxConfig = async () => {
      if (!checkoutState.orderType) return;

      try {
        const config = await adminTaxConfigurationService.getTaxForOrderType(checkoutState.orderType);
        setTaxConfig(config);

        // Calculate tax amount based on subtotal
        if (config && cartState.basket) {
          const subtotal = cartState.basket.subTotal - (cartState.basket.discount || 0) - (cartState.basket.customerDiscount || 0);
          const calculatedTax = subtotal * (config.rate / 100);
          setTaxAmount(calculatedTax);
        } else {
          setTaxAmount(0);
        }
      } catch (error) {
        // eslint-disable-next-line no-console
        console.error('Failed to fetch tax configuration:', error);
        setTaxConfig(null);
        setTaxAmount(0);
      }
    };
    fetchTaxConfig();
  }, [checkoutState.orderType, cartState.basket]);

  // Check prerequisites (but don't redirect if order was just confirmed)
  useEffect(() => {
    // Don't redirect if we have a confirmed order being shown
    if (confirmedOrder) return;

    if (cartState.items.length === 0) {
      router.push('/cart');
    } else if (!checkoutState.orderType) {
      router.push('/checkout/order-type');
    } else if (!checkoutState.customerInfo) {
      router.push('/checkout/customer-info');
    }
  }, [cartState.items.length, checkoutState.orderType, checkoutState.customerInfo, router, confirmedOrder]);

  // Show loading state only if we don't have a confirmed order (modal will display instead)
  if ((cartState.items.length === 0 || !checkoutState.orderType || !checkoutState.customerInfo) && !confirmedOrder) {
    return (
      <main className={styles.container}>
        <div className={styles.loadingState}>
          <Loader2 size={48} className={styles.spinner} />
          <p>{t('loading', 'Loading...')}</p>
        </div>
      </main>
    );
  }

  // If order is confirmed, only show the modal (don't render the form which needs customerInfo)
  if (confirmedOrder) {
    return (
      <>
        <OrderConfirmationModal
          isOpen={showConfirmationModal}
          orderNumber={confirmedOrder.orderNumber}
          customerEmail={confirmedOrder.customerEmail || ''}
          isLoggedIn={isLoggedIn}
          onClose={handleCloseConfirmationModal}
        />
      </>
    );
  }

  const formatPrice = (price: number) => {
    return new Intl.NumberFormat('de-CH', {
      style: 'currency',
      currency: 'CHF',
    }).format(price);
  };

  // Check if customer has active discount
  const customerHasDiscount = hasActiveDiscount(
    (cartState.basket?.customerDiscount || 0) + (cartState.basket?.discount || 0)
  );

  // Format total with special rounding for discounted customers
  const formatTotal = (total: number) => {
    const formattedValue = formatPriceWithRounding(total, customerHasDiscount);
    return `CHF ${formattedValue}`;
  };

  const handlePlaceOrder = async () => {
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
          addressLine1: checkoutState.deliveryAddress.street,
          city: checkoutState.deliveryAddress.city,
          postalCode: checkoutState.deliveryAddress.postalCode,
          country: checkoutState.deliveryAddress.country,
          deliveryInstructions: checkoutState.deliveryAddress.additionalInfo,
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

      // eslint-disable-next-line no-console
      console.log('Order created successfully:', { id: createdOrder.id, orderNumber: createdOrder.orderNumber });

      // Store order info for modal (capture email before clearing checkout state)
      setConfirmedOrder({
        id: createdOrder.id,
        orderNumber: createdOrder.orderNumber,
        customerEmail: checkoutState.customerInfo?.email || '',
      });

      // eslint-disable-next-line no-console
      console.log('Setting confirmed order state');

      // Clear cart and checkout state
      await clearCart();
      clearCheckout();

      // Send confirmation emails to both customer and admin (fire and forget)
      try {
        // eslint-disable-next-line no-console
        console.log('Sending confirmation emails for order:', createdOrder.id);
        await sendOrderConfirmationEmails(createdOrder.id);
        // eslint-disable-next-line no-console
        console.log('Confirmation emails sent');
      } catch (emailError) {
        // eslint-disable-next-line no-console
        console.warn('Failed to send confirmation emails:', emailError);
        // Don't fail the order due to email issues
      }

      // Show success message
      enqueueSnackbar(t('order_placed_success', 'Order placed successfully!'), {
        variant: 'success',
        anchorOrigin: { vertical: 'bottom', horizontal: 'right' },
      });

      // Modal will automatically show via useEffect watching confirmedOrder

    } catch (error) {
      // eslint-disable-next-line no-console
      console.error('Error placing order:', error);

      // Get translated error message
      const errorMessage = getTranslatedOrderError(error, t);

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
    <>
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
            <OrderTypeSection
              orderType={checkoutState.orderType}
              tableNumber={checkoutState.tableNumber}
              deliveryAddress={checkoutState.deliveryAddress || undefined}
            />

            <CustomerInfoSection customerInfo={checkoutState.customerInfo} />

            <OrderItemsList items={cartState.items} formatPrice={formatPrice} />

            <PaymentMethodSelector
              selectedMethod={selectedPaymentMethod}
              onMethodChange={setSelectedPaymentMethod}
            />

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
            <FidelityPointsCheckout
              orderSubtotal={cartState.basket?.subTotal || 0}
              onPointsRedemption={handlePointsRedemption}
            />

            <OrderSummaryCard
              basket={cartState.basket}
              taxConfig={taxConfig}
              taxAmount={taxAmount}
              pointsDiscount={pointsDiscount}
              redeemedPoints={redeemedPoints}
              isSubmitting={isSubmitting}
              submitError={submitError}
              formatPrice={formatPrice}
              formatTotal={formatTotal}
              onPlaceOrder={handlePlaceOrder}
            />
          </div>
        </div>
      </div>
    </main>
  </>
  );
}
