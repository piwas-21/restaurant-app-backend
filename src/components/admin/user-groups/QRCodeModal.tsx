import React from 'react';
import { QRCodeSVG } from 'qrcode.react';
import { useTranslation } from 'react-i18next';
import styles from '@/app/styles/RegisterStaffModal.module.css';
import modalStyles from './QRCodeModal.module.css';

interface QRCodeModalProps {
  isOpen: boolean;
  onClose: () => void;
  qrCodeData: string;
  title: string;
}

const QRCodeModal: React.FC<QRCodeModalProps> = ({ isOpen, onClose, qrCodeData, title }) => {
  const { t } = useTranslation();

  if (!isOpen) return null;

  const handleDownload = () => {
    const svg = document.getElementById('qr-code-svg');
    if (svg) {
      const svgData = new XMLSerializer().serializeToString(svg);
      const canvas = document.createElement('canvas');
      const ctx = canvas.getContext('2d');
      const img = new Image();
      img.onload = () => {
        canvas.width = img.width;
        canvas.height = img.height;
        ctx?.drawImage(img, 0, 0);
        const pngFile = canvas.toDataURL('image/png');
        const downloadLink = document.createElement('a');
        downloadLink.download = `${title.replace(/\s+/g, '_')}_QR.png`;
        downloadLink.href = pngFile;
        downloadLink.click();
      };
      img.src = 'data:image/svg+xml;base64,' + btoa(svgData);
    }
  };

  return (
    <div className={styles.modalOverlay}>
      <div className={`${styles.modalContent} ${modalStyles.qrModalContent}`}>
        <h2>{title}</h2>
        <div className={modalStyles.qrCodeContainer}>
          <QRCodeSVG
            id="qr-code-svg"
            value={qrCodeData}
            size={256}
            level="H"
            includeMargin={true}
          />
        </div>
        <p className={modalStyles.qrCodeText}>
          {qrCodeData}
        </p>
        <div className={`${styles.buttonGroup} ${modalStyles.centeredButtonGroup}`}>
          <button onClick={handleDownload} className={styles.submitButton}>
            {t('download_wallet_pass')} {/* Using this key for now, maybe change to 'Download QR' */}
          </button>
          <button onClick={onClose} className={styles.cancelButton}>
            {t('close')}
          </button>
        </div>
      </div>
    </div>
  );
};

export default QRCodeModal;
