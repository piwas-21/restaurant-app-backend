'use client';

import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { CheckCircle, Home, ShoppingBag } from 'lucide-react';
import styles from './OrderConfirmationModal.module.css';

interface OrderConfirmationModalProps {
  isOpen: boolean;
  orderNumber: string;
  customerEmail: string;
  isLoggedIn: boolean;
  onClose?: () => void;
}

export default function OrderConfirmationModal({
  isOpen,
  orderNumber,
  customerEmail,
  isLoggedIn,
  onClose
}: OrderConfirmationModalProps) {
  const router = useRouter();
  const { t } = useTranslation();

  if (!isOpen) return null;

  const handleViewOrders = () => {
    router.push('/orders');
  };

  const handleBackToMenu = () => {
    router.push('/menu');
  };

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <div className={styles.iconContainer}>
          <div className={styles.successIcon}>
            <CheckCircle size={80} />
          </div>
        </div>

        <h2 className={styles.title}>{t('order_confirmed', 'Order Confirmed!')}</h2>

        <p className={styles.message}>
          {t('order_confirmation_message', 'Thank you for your order. We have received it and will start preparing it shortly.')}
        </p>

        <div className={styles.orderNumberCard}>
          <span className={styles.orderNumberLabel}>{t('order_number', 'Order Number')}</span>
          <span className={styles.orderNumberValue}>{orderNumber}</span>
        </div>

        <div className={styles.emailInfo}>
          <span className={styles.emailLabel}>{t('confirmation_email_sent_to', 'Confirmation email sent to:')}</span>
          <span className={styles.email}>{customerEmail}</span>
        </div>

        <div className={styles.actions}>
          {isLoggedIn ? (
            <>
              <button className={styles.primaryButton} onClick={handleViewOrders}>
                <ShoppingBag size={20} />
                {t('track_order', 'Track Order')}
              </button>
              <button className={styles.secondaryButton} onClick={handleBackToMenu}>
                <Home size={20} />
                {t('back_to_menu', 'Back to Menu')}
              </button>
            </>
          ) : (
            <>
              <button className={styles.primaryButton} onClick={handleBackToMenu}>
                <Home size={20} />
                {t('back_to_menu', 'Back to Menu')}
              </button>
              <p className={styles.guestNote}>
                {t('login_to_track_order', '💡 Login to your account to track your order status in real-time.')}
              </p>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
