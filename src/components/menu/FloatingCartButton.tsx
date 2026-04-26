/**
 * Floating Cart Button Component
 *
 * Always-visible cart button that floats at the bottom of the screen
 * Shows item count, total price, and provides quick access to cart
 */

'use client';

import React, { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { ShoppingCart } from 'lucide-react';
import styles from './FloatingCartButton.module.css';

interface FloatingCartButtonProps {
  itemCount: number;
  totalPrice: number;
  onAnimate?: boolean;
}

export default function FloatingCartButton({
  itemCount,
  totalPrice,
  onAnimate = false,
}: FloatingCartButtonProps) {
  const router = useRouter();
  const { t } = useTranslation();
  const [isAnimating, setIsAnimating] = useState(false);
  const [isVisible, setIsVisible] = useState(false);

  // Show button with slight delay for smooth entrance
  useEffect(() => {
    const timer = setTimeout(() => setIsVisible(true), 300);
    return () => clearTimeout(timer);
  }, []);

  // Trigger animation when cart updates
  useEffect(() => {
    if (onAnimate && itemCount > 0) {
      setIsAnimating(true);
      const timer = setTimeout(() => setIsAnimating(false), 600);
      return () => clearTimeout(timer);
    }
  }, [onAnimate, itemCount]);

  // Don't render if no items (optional: can always show with 0 items)
  if (itemCount === 0) {
    return null;
  }

  const handleClick = () => {
    router.push('/cart');
  };

  const formatPrice = (price: number) => {
    return new Intl.NumberFormat('de-CH', {
      style: 'currency',
      currency: 'CHF',
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(price);
  };

  return (
    <button
      onClick={handleClick}
      className={`${styles.floatingButton} ${isVisible ? styles.visible : ''} ${isAnimating ? styles.pulse : ''}`}
      aria-label={t('floating_cart_button_label', `View cart, ${itemCount} items, ${formatPrice(totalPrice)}`)}
      title={t('view_cart_checkout_button', 'View Cart')}
    >
      <div className={styles.iconContainer}>
        <ShoppingCart size={24} />
        {itemCount > 0 && (
          <span className={styles.badge} aria-label={t('cart_items_count', `${itemCount} items`)}>
            {itemCount}
          </span>
        )}
      </div>

      <div className={styles.priceContainer}>
        <span className={styles.totalLabel}>{t('cart_total_label', 'Total')}</span>
        <span className={styles.totalPrice}>{formatPrice(totalPrice)}</span>
      </div>
    </button>
  );
}
