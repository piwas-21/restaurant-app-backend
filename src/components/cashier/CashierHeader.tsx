import React, { useState, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { RefreshCw, QrCode, FileBarChart /*Printer, Settings*/ } from 'lucide-react';
import { NotificationSoundType } from '@/hooks/useNotification';
import SoundSelector from './SoundSelector';
import styles from './CashierHeader.module.css';

interface CashierHeaderProps {
  isConnected: boolean;
  isRefreshing: boolean;
  audioEnabled: boolean;
  audioBlockedByPolicy?: boolean; // NEW: Warn when audio is blocked
  soundType: NotificationSoundType;
  repeatUntilMouseMoves: boolean;
  onRefresh: () => void;
  onToggleAudio: () => void;
  onSoundTypeChange: (type: NotificationSoundType) => void;
  onTestSound: (type: NotificationSoundType) => void;
  onToggleRepeat: () => void;
  onOpenQRScanner: () => void;
  onOpenZReport?: () => void;
  onOpenDiagnostics?: () => void; // NEW: Open diagnostic panel
  // autoPrintEnabled?: boolean;
  // onToggleAutoPrint?: () => void;
  // onOpenAutoPrintSettings?: () => void;
}

export default function CashierHeader({
  isConnected,
  isRefreshing,
  audioEnabled,
  audioBlockedByPolicy,
  soundType,
  repeatUntilMouseMoves,
  onRefresh,
  onToggleAudio,
  onSoundTypeChange,
  onTestSound,
  onToggleRepeat,
  onOpenQRScanner,
  onOpenZReport,
  onOpenDiagnostics
}: CashierHeaderProps) {
  const { t } = useTranslation();
  const [showSoundSelector, setShowSoundSelector] = useState(false);

  // Close sound selector when clicking outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (showSoundSelector) {
        const target = event.target as HTMLElement;
        if (!target.closest('[data-sound-selector]')) {
          setShowSoundSelector(false);
        }
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [showSoundSelector]);

  return (
    <div className={styles.header}>
      <div className={styles.headerLeft}>
        <h1 className={styles.title}>{t('cashier.title') || 'Cashier'}</h1>
      </div>

      <div className={styles.headerRight}>
        {/* Connection Status */}
        <div className={`${styles.connectionStatus} ${isConnected ? styles.connected : styles.disconnected}`}>
          <div className={`${styles.connectionDot} ${isConnected ? styles.dotConnected : styles.dotDisconnected}`} />
          <span>
            {isConnected ? t('cashier.connected') || 'Connected' : t('cashier.disconnected') || 'Disconnected'}
          </span>
        </div>

        {/* Audio Blocked Warning */}
        {audioBlockedByPolicy && (
          <div className={styles.connectionStatus} style={{ backgroundColor: '#fff3cd', borderColor: '#ffc107', color: '#856404' }}>
            <span>⚠️ {t('audio_blocked') || 'Audio Blocked'}</span>
          </div>
        )}

        {/* Diagnostics Button */}
        {onOpenDiagnostics && (
          <button
            className={styles.button}
            onClick={onOpenDiagnostics}
            title={t('diagnostics') || 'System Diagnostics'}
            style={{ backgroundColor: '#607d8b' }}
          >
            🔍 {t('diagnostics') || 'Diagnostics'}
          </button>
        )}

        {/* Sound Toggle Button */}
        <button
          className={styles.button}
          onClick={onToggleAudio}
          title={audioEnabled
            ? (t('cashier.disable_sound') || 'Disable notification sounds')
            : (t('cashier.enable_sound') || 'Enable notification sounds')
          }
          style={{
            backgroundColor: audioEnabled ? '#4caf50' : '#ff9800'
          }}
        >
          {audioEnabled ? '🔕' : '🔔'} {audioEnabled
            ? (t('cashier.disable_sound') || 'Disable Sound')
            : (t('cashier.enable_sound') || 'Enable Sound')
          }
        </button>

        {/* Sound Selector */}
        {audioEnabled && (
          <div className={styles.soundSelectorWrapper} data-sound-selector>
            <button
              className={styles.button}
              onClick={() => setShowSoundSelector(!showSoundSelector)}
              title={t('select_sound') || 'Select notification sound'}
              style={{ backgroundColor: '#9c27b0' }}
            >
              🎵 {t('sound') || 'Sound'}
            </button>

            <SoundSelector
              isOpen={showSoundSelector}
              onClose={() => setShowSoundSelector(false)}
              soundType={soundType}
              onSoundTypeChange={onSoundTypeChange}
              onTestSound={onTestSound}
              repeatUntilMouseMoves={repeatUntilMouseMoves}
              onToggleRepeat={onToggleRepeat}
            />
          </div>
        )}

        {/* QR Scanner Button */}
        <button
          className={styles.button}
          onClick={onOpenQRScanner}
          title={t('cashier.scan_qr_code') || 'Scan QR Code'}
        >
          <QrCode size={16} />
          {t('cashier.scan_qr') || 'Scan QR'}
        </button>

        {/* Auto-Print Button */}
        {/* <button
          className={styles.button}
          onClick={onToggleAutoPrint}
          title={autoPrintEnabled
            ? 'Auto-print enabled - Click to configure'
            : 'Auto-print disabled - Click to enable'}
          style={{
            backgroundColor: autoPrintEnabled ? '#4caf50' : '#757575'
          }}
        >
          <Printer size={16} />
          Auto-Print {autoPrintEnabled ? 'ON' : 'OFF'}
        </button> */}

        {/* Auto-Print Settings Button */}
        {/* <button
          className={styles.button}
          onClick={onOpenAutoPrintSettings}
          title="Configure auto-print settings"
          style={{ backgroundColor: '#607d8b' }}
        >
          <Settings size={16} />
          Print Settings
        </button> */}

        {/* Z-Report Button */}
        {onOpenZReport && (
          <button
            className={styles.button}
            onClick={onOpenZReport}
            title={t('cashier.zreport.title') || 'Z-Report'}
          >
            <FileBarChart size={16} />
            {t('cashier.zreport.title') || 'Z-Report'}
          </button>
        )}

        {/* Refresh Button */}
        <button
          className={`${styles.button} ${isRefreshing ? styles.loading : ''}`}
          onClick={onRefresh}
          disabled={isRefreshing}
          title={t('cashier.refresh') || 'Refresh orders'}
        >
          <RefreshCw size={16} />
          {isRefreshing ? t('cashier.refreshing') || 'Refreshing...' : t('cashier.refresh') || 'Refresh'}
        </button>
      </div>
    </div>
  );
}
