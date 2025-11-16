/**
 * Order Summary Card Component
 *
 * Displays pricing breakdown, tax, total, and place order button
 */

import React from 'react';
import { useTranslation } from 'react-i18next';
import { useRouter } from 'next/navigation';
import { ArrowRight, AlertCircle, Loader2 } from 'lucide-react';
import type { TaxConfiguration } from '@/services/adminTaxConfigurationService';
import styles from './OrderSummaryCard.module.css';

interface Basket {
  subTotal: number;
  discount?: number;
  customerDiscount?: number;
  deliveryFee?: number;
  total: number;
}

interface OrderSummaryCardProps {
  basket: Basket | null;
  taxConfig: TaxConfiguration | null;
  taxAmount: number;
  pointsDiscount: number;
  redeemedPoints: number;
  isSubmitting: boolean;
  submitError: string;
  formatPrice: (price: number) => string;
  formatTotal: (total: number) => string;
  onPlaceOrder: () => void;
}

export default function OrderSummaryCard({
  basket,
  taxConfig,
  taxAmount,
  pointsDiscount,
  redeemedPoints,
  isSubmitting,
  submitError,
  formatPrice,
  formatTotal,
  onPlaceOrder,
}: OrderSummaryCardProps) {
  const { t } = useTranslation();
  const router = useRouter();

  return (
    <div className={styles.summaryCard}>
      <h2 className={styles.summaryTitle}>{t('order_summary', 'Order Summary')}</h2>

      <div className={styles.summaryRows}>
        <div className={styles.summaryRow}>
          <span>{t('subtotal', 'Subtotal')}</span>
          <span>{formatPrice(basket?.subTotal || 0)}</span>
        </div>

        {(basket?.discount ?? 0) > 0 && (
          <div className={`${styles.summaryRow} ${styles.discount}`}>
            <span>{t('discount', 'Promo Discount')}</span>
            <span>-{formatPrice(basket?.discount || 0)}</span>
          </div>
        )}

        {(basket?.customerDiscount ?? 0) > 0 && (
          <div className={`${styles.summaryRow} ${styles.discount}`}>
            <span>{t('customer_discount', 'Customer Discount')}</span>
            <span>-{formatPrice(basket?.customerDiscount || 0)}</span>
          </div>
        )}

        {pointsDiscount > 0 && (
          <div className={`${styles.summaryRow} ${styles.discount}`}>
            <span>{t('points_discount', 'Points Discount')} ({redeemedPoints} pts)</span>
            <span>-{formatPrice(pointsDiscount)}</span>
          </div>
        )}

        {(basket?.deliveryFee ?? 0) > 0 && (
          <div className={styles.summaryRow}>
            <span>{t('delivery_fee', 'Delivery Fee')}</span>
            <span>{formatPrice(basket?.deliveryFee || 0)}</span>
          </div>
        )}

        {taxAmount > 0 && (
          <div className={styles.summaryRow}>
            <span>
              {taxConfig ? `${taxConfig.name} (${taxConfig.rate}%)` : t('tax', 'Tax')}
            </span>
            <span>{formatPrice(taxAmount)}</span>
          </div>
        )}
      </div>

      <div className={styles.summaryTotal}>
        <span>{t('total', 'Total')}</span>
        <span className={styles.totalAmount}>
          {formatTotal((basket?.total || 0) - pointsDiscount)}
        </span>
      </div>

      {submitError && (
        <div className={styles.errorAlert}>
          <AlertCircle size={20} />
          <p>{submitError}</p>
        </div>
      )}

      <button
        onClick={onPlaceOrder}
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
  );
}
