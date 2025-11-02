'use client';

import React, { useState, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { QRCodeSVG } from 'qrcode.react';
import { X, Download, Printer, RefreshCw } from 'lucide-react';
import { generateQRCodeURL, downloadQRCode, printQRCode, formatQRGeneratedDate } from '@/utils/qrCode';
import styles from './TableQRCodeModal.module.css';

interface TableQRCodeModalProps {
  isOpen: boolean;
  onClose: () => void;
  tableId: string;
  tableNumber: string;
  qrCodeData?: string;
  qrCodeGeneratedAt?: string;
  onRegenerate: () => Promise<void>;
}

export default function TableQRCodeModal({
  isOpen,
  onClose,
  tableNumber,
  qrCodeData,
  qrCodeGeneratedAt,
  onRegenerate,
}: TableQRCodeModalProps) {
  const { t } = useTranslation();
  const [isRegenerating, setIsRegenerating] = useState(false);
  const qrRef = useRef<HTMLDivElement>(null);

  if (!isOpen) return null;

  // Generate the full URL for QR code
  const qrUrl = qrCodeData ? generateQRCodeURL(qrCodeData) : '';

  const handleDownload = () => {
    if (!qrRef.current) return;

    const svg = qrRef.current.querySelector('svg');
    if (!svg) return;

    // Convert SVG to canvas
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const svgData = new XMLSerializer().serializeToString(svg);
    const img = new Image();
    const svgBlob = new Blob([svgData], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(svgBlob);

    img.onload = () => {
      canvas.width = img.width;
      canvas.height = img.height;
      ctx.fillStyle = 'white';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(img, 0, 0);

      // Use utility function for download
      downloadQRCode(canvas, `table-${tableNumber}-qr-code`);
      URL.revokeObjectURL(url);
    };

    img.src = url;
  };

  const handlePrint = () => {
    if (!qrRef.current) return;

    const svg = qrRef.current.querySelector('svg');
    if (!svg) return;

    // Convert SVG to canvas
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const svgData = new XMLSerializer().serializeToString(svg);
    const img = new Image();
    const svgBlob = new Blob([svgData], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(svgBlob);

    img.onload = () => {
      canvas.width = img.width;
      canvas.height = img.height;
      ctx.fillStyle = 'white';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(img, 0, 0);

      // Use utility function for print
      printQRCode(canvas, tableNumber);
      URL.revokeObjectURL(url);
    };

    img.src = url;
  };

  const handleRegenerate = async () => {
    setIsRegenerating(true);
    try {
      await onRegenerate();
    } finally {
      setIsRegenerating(false);
    }
  };

  return (
    <div className={styles.modalOverlay} onClick={onClose}>
      <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
        <div className={styles.modalHeader}>
          <h2>{t('qr_code_for_table', { tableNumber })}</h2>
          <button
            onClick={onClose}
            className={styles.closeButton}
            aria-label="Close modal"
          >
            <X size={24} />
          </button>
        </div>

        <div className={styles.modalBody}>
          {qrCodeData ? (
            <>
              <div className={styles.qrCodeContainer} ref={qrRef}>
                <QRCodeSVG
                  value={qrUrl}
                  size={300}
                  level="H"
                  includeMargin={true}
                />
              </div>

              <p className={styles.infoText}>{t('qr_code_info')}</p>

              {qrCodeGeneratedAt && (
                <p className={styles.generatedDate}>
                  {t('qr_code_generated')}: {formatQRGeneratedDate(qrCodeGeneratedAt)}
                </p>
              )}

              <div className={styles.actions}>
                <button
                  onClick={handleDownload}
                  className={styles.actionButton}
                >
                  <Download size={20} />
                  {t('download_qr_code')}
                </button>

                <button
                  onClick={handlePrint}
                  className={styles.actionButton}
                >
                  <Printer size={20} />
                  {t('print_qr_code')}
                </button>

                <button
                  onClick={handleRegenerate}
                  className={`${styles.actionButton} ${styles.regenerateButton}`}
                  disabled={isRegenerating}
                >
                  <RefreshCw size={20} className={isRegenerating ? styles.spinning : ''} />
                  {t('regenerate_qr')}
                </button>
              </div>
            </>
          ) : (
            <div className={styles.noQrCode}>
              <p>{t('no_qr_code')}</p>
              <button
                onClick={handleRegenerate}
                className={styles.generateButton}
                disabled={isRegenerating}
              >
                <RefreshCw size={20} className={isRegenerating ? styles.spinning : ''} />
                {t('generate_qr_code')}
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
