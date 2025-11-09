/**
 * Order Items List Component
 *
 * Displays all items in the cart with images, quantities, and prices
 */

import React from 'react';
import { useTranslation } from 'react-i18next';
import { useRouter } from 'next/navigation';
import { ShoppingBag, Edit } from 'lucide-react';
import Image from 'next/image';
import styles from './OrderItemsList.module.css';

interface CartItem {
  id?: string;
  productName?: string;
  productImageUrl?: string;
  variationName?: string;
  quantity: number;
  unitPrice: number;
  itemTotal: number;
  specialInstructions?: string;
}

interface OrderItemsListProps {
  items: CartItem[];
  formatPrice: (price: number) => string;
}

export default function OrderItemsList({ items, formatPrice }: OrderItemsListProps) {
  const { t } = useTranslation();
  const router = useRouter();

  return (
    <section className={styles.section}>
      <div className={styles.sectionHeader}>
        <h2 className={styles.sectionTitle}>
          <ShoppingBag size={20} />
          {t('order_items', 'Order Items')} ({items.length})
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
        {items.map((item, index) => (
          <div key={item.id || `item-${index}`} className={styles.cartItem}>
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
  );
}
