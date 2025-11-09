/**
 * Order Type Section Component
 *
 * Displays order type, table number (for dine-in), or delivery address (for delivery)
 */

import React from 'react';
import { useTranslation } from 'react-i18next';
import { useRouter } from 'next/navigation';
import { ShoppingBag, MapPin, Edit } from 'lucide-react';
import styles from './OrderTypeSection.module.css';

interface DeliveryAddress {
  street: string;
  city: string;
  postalCode: string;
  country: string;
  additionalInfo?: string;
}

interface OrderTypeSectionProps {
  orderType: 'DineIn' | 'Takeaway' | 'Delivery';
  tableNumber?: string;
  deliveryAddress?: DeliveryAddress;
}

export default function OrderTypeSection({
  orderType,
  tableNumber,
  deliveryAddress,
}: OrderTypeSectionProps) {
  const { t } = useTranslation();
  const router = useRouter();

  return (
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
            {orderType === 'DineIn' && t('order_type_dine_in', 'Dine In')}
            {orderType === 'Takeaway' && t('order_type_takeaway', 'Takeaway')}
            {orderType === 'Delivery' && t('order_type_delivery', 'Delivery')}
          </span>
        </div>
        {orderType === 'DineIn' && tableNumber && (
          <div className={styles.infoRow}>
            <span className={styles.infoLabel}>{t('table_number', 'Table Number')}:</span>
            <span className={styles.infoValue}>{tableNumber}</span>
          </div>
        )}
        {orderType === 'Delivery' && deliveryAddress && (
          <div className={styles.deliveryAddress}>
            <MapPin size={18} className={styles.addressIcon} />
            <div>
              <p>{deliveryAddress.street}</p>
              <p>{deliveryAddress.postalCode} {deliveryAddress.city}</p>
              <p>{deliveryAddress.country}</p>
              {deliveryAddress.additionalInfo && (
                <p className={styles.additionalInfo}>{deliveryAddress.additionalInfo}</p>
              )}
            </div>
          </div>
        )}
      </div>
    </section>
  );
}
