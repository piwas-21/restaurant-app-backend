import React, { useState, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { X, QrCode, CheckCircle, AlertCircle } from 'lucide-react';
import styles from '@/app/styles/CashierPage.module.css'; // Reusing cashier styles
import { validateQRCode } from '@/services/userGroupService';
import { QRCodeValidationResult } from '@/types/userGroupTypes';

interface QRScannerDialogProps {
  isOpen: boolean;
  onClose: () => void;
  onApplyDiscount?: (result: QRCodeValidationResult) => void;
}

const QRScannerDialog: React.FC<QRScannerDialogProps> = ({
  isOpen,
  onClose,
  onApplyDiscount
}) => {
  const { t } = useTranslation();
  const [qrCode, setQrCode] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [result, setResult] = useState<QRCodeValidationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (isOpen) {
      setQrCode('');
      setResult(null);
      setError(null);
      // Focus input for scanner
      setTimeout(() => {
        inputRef.current?.focus();
      }, 100);
    }
  }, [isOpen]);

  const handleScan = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!qrCode.trim()) return;

    setIsLoading(true);
    setError(null);
    setResult(null);

    try {
      const response = await validateQRCode(qrCode);
      if (response.success && response.data) {
        setResult(response.data);
      } else {
        setError(response.message || t('cashier.invalid_qr_code'));
      }
    } catch (err) {
      console.error('QR Validation failed:', err);
      setError(t('cashier.validation_failed'));
    } finally {
      setIsLoading(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div className={styles.dialogOverlay}>
      <div className={styles.dialogContent} style={{ maxWidth: '500px' }}>
        <div className={styles.dialogHeader}>
          <h2>{t('cashier.scan_qr_code')}</h2>
          <button className={styles.closeButton} onClick={onClose}>
            <X size={24} />
          </button>
        </div>

        <div className={styles.dialogBody}>
          {!result ? (
            <form onSubmit={handleScan} style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
              <div style={{ textAlign: 'center', padding: '20px', background: '#f8f9fa', borderRadius: '8px' }}>
                <QrCode size={64} color="#6c757d" />
                <p style={{ marginTop: '10px', color: '#6c757d' }}>
                  {t('cashier.scan_instruction') || 'Scan QR code with handheld scanner or enter manually'}
                </p>
              </div>

              <div className={styles.formGroup}>
                <input
                  ref={inputRef}
                  type="text"
                  className={styles.input}
                  placeholder={t('cashier.enter_qr_code') || 'Enter QR Code'}
                  value={qrCode}
                  onChange={(e) => setQrCode(e.target.value)}
                  disabled={isLoading}
                />
              </div>

              {error && (
                <div className="alert alert-error" style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                  <AlertCircle size={20} />
                  {error}
                </div>
              )}

              <div className={styles.dialogFooter}>
                <button
                  type="button"
                  className={styles.cancelButton}
                  onClick={onClose}
                  disabled={isLoading}
                >
                  {t('cancel')}
                </button>
                <button
                  type="submit"
                  className={styles.confirmButton}
                  disabled={isLoading || !qrCode.trim()}
                >
                  {isLoading ? t('validating...') : t('validate')}
                </button>
              </div>
            </form>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
              <div className="alert alert-success" style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                <CheckCircle size={20} />
                {t('cashier.valid_membership')}
              </div>

              <div style={{ padding: '15px', border: '1px solid #eee', borderRadius: '8px' }}>
                <h3 style={{ margin: '0 0 10px 0' }}>{t('member_details')}</h3>
                <p><strong>{t('name')}:</strong> {result.membership?.userName}</p>
                <p><strong>{t('group')}:</strong> {result.group?.name}</p>
                <p><strong>{t('status')}:</strong> {result.membership?.isActive ? t('active') : t('inactive')}</p>
              </div>

              <div>
                <h3 style={{ margin: '0 0 10px 0' }}>{t('available_discounts')}</h3>
                {result.applicableDiscounts && result.applicableDiscounts.length > 0 ? (
                  <ul style={{ paddingLeft: '20px', margin: 0 }}>
                    {result.applicableDiscounts.map((discount) => (
                      <li key={discount.id} style={{ marginBottom: '5px' }}>
                        <strong>{discount.name}</strong>: 
                        {discount.type === 'Percentage' ? ` ${discount.value}%` : ` CHF ${discount.value}`}
                        {discount.minimumOrderAmount && ` (Min Order: CHF ${discount.minimumOrderAmount})`}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p style={{ color: '#666', fontStyle: 'italic' }}>{t('no_discounts_available')}</p>
                )}
              </div>

              <div className={styles.dialogFooter}>
                <button
                  type="button"
                  className={styles.cancelButton}
                  onClick={() => {
                    setResult(null);
                    setQrCode('');
                    setTimeout(() => inputRef.current?.focus(), 100);
                  }}
                >
                  {t('scan_another')}
                </button>
                <button
                  type="button"
                  className={styles.confirmButton}
                  onClick={() => {
                    if (onApplyDiscount) onApplyDiscount(result);
                    onClose();
                  }}
                >
                  {t('close')}
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default QRScannerDialog;
