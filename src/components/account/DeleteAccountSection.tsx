import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AlertTriangle, Trash2, CheckCircle, XCircle } from 'lucide-react';
import { requestAccountDeletion } from '@/services/authService';
import styles from './DeleteAccountSection.module.css';

export default function DeleteAccountSection() {
  const { t } = useTranslation();
  const [isDeleting, setIsDeleting] = useState(false);
  const [successMessage, setSuccessMessage] = useState('');
  const [errorMessage, setErrorMessage] = useState('');

  const handleDeleteRequest = async () => {
    setIsDeleting(true);
    setErrorMessage('');
    setSuccessMessage('');

    try {
      const response = await requestAccountDeletion();
      if (response.success) {
        setSuccessMessage(
          t(
            'delete_account_request_success',
            'We sent a confirmation email. Please check your inbox to proceed with deletion.',
          ),
        );
      } else {
        setErrorMessage(response.message || t('delete_account_request_failed', 'Failed to request account deletion.'));
      }
    } catch {
      setErrorMessage(t('unexpected_error', 'An unexpected error occurred.'));
    } finally {
      setIsDeleting(false);
    }
  };

  return (
    <section className={styles.dangerZone}>
      <div className={styles.dangerZoneHeader}>
        <div className={styles.dangerZoneIcon}>
          <AlertTriangle size={20} />
        </div>
        <h2 className={styles.dangerZoneTitle}>{t('danger_zone_title', 'Danger Zone')}</h2>
      </div>

      <div className={styles.dangerZoneContent}>
        <p className={styles.warningText}>
          {t('delete_account_warning', 'Once you delete your account, there is no going back. Please be certain.')}
        </p>

        <ul className={styles.infoList}>
          <li>{t('delete_account_info_1', 'All your personal data will be permanently removed')}</li>
          <li>{t('delete_account_info_2', 'Your order history will be anonymized')}</li>
          <li>{t('delete_account_info_3', 'Active reservations will be cancelled')}</li>
          <li>{t('delete_account_info_4', 'This action cannot be undone')}</li>
        </ul>

        {successMessage && (
          <div className={`${styles.alertBox} ${styles.success}`}>
            <CheckCircle size={20} className={styles.alertIcon} />
            <span>{successMessage}</span>
          </div>
        )}

        {errorMessage && (
          <div className={`${styles.alertBox} ${styles.error}`}>
            <XCircle size={20} className={styles.alertIcon} />
            <span>{errorMessage}</span>
          </div>
        )}

        <div className={styles.buttonContainer}>
          <button onClick={handleDeleteRequest} disabled={isDeleting} className={styles.deleteButton}>
            <Trash2 size={18} />
            {isDeleting ? t('processing', 'Processing...') : t('delete_account_button', 'Delete My Account')}
          </button>
          <span className={styles.helpText}>{t('delete_account_help', 'You will receive a confirmation email')}</span>
        </div>
      </div>
    </section>
  );
}
