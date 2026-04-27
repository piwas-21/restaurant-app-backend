'use client';

import { useRouter } from 'next/navigation';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/components/AuthContext'; // Added import
import styles from './ReservationSuccessModal.module.css';

interface ReservationSuccessModalProps {
  isOpen: boolean;
  onClose: () => void;
  customerEmail: string;
  numberOfTables: number;
}

export default function ReservationSuccessModal({
  isOpen,
  onClose,
  customerEmail,
  numberOfTables,
}: ReservationSuccessModalProps) {
  const router = useRouter();
  const { t } = useTranslation();
  const { user } = useAuth(); // Added useAuth hook

  if (!isOpen) return null;

  const handleViewReservations = () => {
    router.push('/my-reservations');
  };

  const handleLogin = () => {
    router.push('/auth/login');
  };

  const handleMakeAnother = () => {
    onClose();
  };

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
        <button className={styles.closeButton} onClick={onClose}>
          ✕
        </button>

        <div className={styles.iconContainer}>
          <div className={styles.clockIcon}>⏳</div>
        </div>

        <h2 className={styles.title}>{t('reservation_pending_confirmation', 'Pending Confirmation')}</h2>

        <p className={styles.message}>
          {t(
            'reservation_pending_message',
            numberOfTables > 1
              ? 'Your reservations are currently pending. Our team will review your request and send you a confirmation email shortly.'
              : 'Your reservation is currently pending. Our team will review your request and send you a confirmation email shortly.',
          )}
        </p>

        <div className={styles.emailInfo}>
          <span className={styles.emailLabel}>{t('confirmation_email_sent_to', 'Confirmation will be sent to:')}</span>
          <span className={styles.email}>{customerEmail}</span>
        </div>

        <div className={styles.actions}>
          {user ? (
            <>
              <button className={styles.primaryButton} onClick={handleViewReservations}>
                {t('view_my_reservations', 'View My Reservations')}
              </button>
              <button className={styles.secondaryButton} onClick={handleMakeAnother}>
                {t('make_another_reservation', 'Make Another Reservation')}
              </button>
            </>
          ) : (
            <>
              <button className={styles.primaryButton} onClick={handleLogin}>
                {t('login_to_track_reservation', 'Login to Track Your Reservation')}
              </button>
              <button className={styles.secondaryButton} onClick={handleMakeAnother}>
                {t('make_another_reservation', 'Make Another Reservation')}
              </button>
              <p className={styles.guestNote}>
                {t(
                  'create_account_to_track',
                  '💡 Create an account to easily track all your reservations and manage them online.',
                )}
              </p>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
