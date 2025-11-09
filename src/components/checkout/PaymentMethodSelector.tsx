/**
 * Payment Method Selector Component
 *
 * Displays available payment methods and allows user to select one
 */

import React from 'react';
import { useTranslation } from 'react-i18next';
import { CreditCard, Info, CheckCircle } from 'lucide-react';
import { PaymentMethod } from '@/types/order';
import { PAYMENT_METHODS } from '@/config/paymentMethods';
import styles from './PaymentMethodSelector.module.css';

interface PaymentMethodSelectorProps {
  selectedMethod: PaymentMethod;
  onMethodChange: (method: PaymentMethod) => void;
}

export default function PaymentMethodSelector({
  selectedMethod,
  onMethodChange,
}: PaymentMethodSelectorProps) {
  const { t } = useTranslation();

  return (
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
        {PAYMENT_METHODS.map((method) => {
          const Icon = method.icon;
          const isDisabled = method.disabled;

          return (
            <label
              key={method.value}
              className={`${styles.paymentMethod} ${
                selectedMethod === method.value ? styles.selected : ''
              } ${isDisabled ? styles.disabled : ''}`}
            >
              <input
                type="radio"
                name="paymentMethod"
                value={method.value}
                checked={selectedMethod === method.value}
                onChange={() => !isDisabled && onMethodChange(method.value)}
                className={styles.paymentRadio}
                disabled={isDisabled}
              />
              <div className={styles.paymentIcon}>
                <Icon size={24} />
              </div>
              <div className={styles.paymentInfo}>
                <span className={styles.paymentLabel}>
                  {t(method.labelKey, method.label)}
                  {isDisabled && <span className={styles.comingSoon}> ({t('coming_soon', 'Coming Soon')})</span>}
                </span>
                <span className={styles.paymentDescription}>
                  {t(method.descriptionKey, method.description)}
                </span>
              </div>
              {selectedMethod === method.value && !isDisabled && (
                <CheckCircle size={20} className={styles.checkmark} />
              )}
            </label>
          );
        })}
      </div>
    </section>
  );
}
