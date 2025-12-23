'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import { 
  Wifi, 
  WifiOff, 
  Volume2, 
  VolumeX, 
  RefreshCw, 
  Play, 
  Unlock, 
  Info, 
  X,
  Monitor,
  AlertCircle
} from 'lucide-react';
import styles from './CashierDiagnostics.module.css';

interface CashierDiagnosticsProps {
  // SSE Connection diagnostics
  sseConnected: boolean;
  sseConnectionState: 'connecting' | 'connected' | 'disconnected' | 'error';
  sseLastEventTime: Date | null;
  sseError: string | null;
  
  // Audio diagnostics
  audioEnabled: boolean;
  audioReady: boolean;
  audioBlockedByPolicy: boolean;
  
  // Actions
  onTestSound: () => void;
  onEnableAudio: () => void;
  onRefreshConnection: () => void;
  onClose?: () => void;
}

export default function CashierDiagnostics({
  sseConnected,
  sseConnectionState,
  sseLastEventTime,
  sseError,
  audioEnabled,
  audioReady,
  audioBlockedByPolicy,
  onTestSound,
  onEnableAudio,
  onRefreshConnection,
  onClose
}: CashierDiagnosticsProps) {
  const { t } = useTranslation();

  const getTimeSinceLastEvent = () => {
    if (!sseLastEventTime) return 'Never';
    const seconds = Math.floor((new Date().getTime() - sseLastEventTime.getTime()) / 1000);
    if (seconds < 60) return `${seconds}s ago`;
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    return `${hours}h ago`;
  };

  const getConnectionStatusClass = () => {
    switch (sseConnectionState) {
      case 'connected': return styles.badgeConnected;
      case 'connecting': return styles.badgeConnecting;
      case 'error': return styles.badgeError;
      default: return '';
    }
  };

  const getAudioStatusClass = () => {
    if (!audioEnabled) return '';
    if (audioReady) return styles.badgeConnected;
    if (audioBlockedByPolicy) return styles.badgeConnecting;
    return '';
  };

  const isFirefox = typeof navigator !== 'undefined' && navigator.userAgent.includes('Firefox');
  const isWindows = typeof navigator !== 'undefined' && navigator.platform.includes('Win');

  return (
    <div className={styles.panel}>
      {/* Header */}
      <div className={styles.header}>
        <h3>
          <Monitor size={18} />
          {t('diagnostics') || 'Diagnostics'}
        </h3>
        {onClose && (
          <button onClick={onClose} className={styles.closeButton} title="Close">
            <X size={20} />
          </button>
        )}
      </div>

      <div className={styles.content}>
        {/* Environment Info */}
        <div className={styles.infoCard}>
          <div className={styles.row}>
            <span className={styles.label}>{t('environment') || 'Environment'}</span>
            <span className={styles.value}>
              {isFirefox ? 'Firefox' : 'Browser'} / {isWindows ? 'Windows' : 'OS'}
            </span>
          </div>
        </div>

        {/* Real-time Connection */}
        <div className={styles.section}>
          <div className={styles.sectionHeader}>
            <div className={`${styles.dot} ${
              sseConnectionState === 'connected' ? styles.dotConnected :
              sseConnectionState === 'connecting' ? styles.dotConnecting :
              sseConnectionState === 'error' ? styles.dotError : styles.dotInactive
            }`} />
            <h4 className={styles.sectionTitle}>{t('real_time_connection') || 'Real-time Connection'}</h4>
          </div>

          <div className={styles.infoCard}>
            <div className={styles.row}>
              <span className={styles.label}>{t('status') || 'Status'}</span>
              <span className={`${styles.badge} ${getConnectionStatusClass()}`}>
                {sseConnectionState}
              </span>
            </div>
            <div className={styles.row}>
              <span className={styles.label}>{t('last_activity') || 'Last Activity'}</span>
              <span className={styles.value}>{getTimeSinceLastEvent()}</span>
            </div>
            {sseError && (
              <div className={styles.errorBox}>
                <AlertCircle size={14} className={styles.errorIcon} />
                <p className={styles.errorText}>{sseError}</p>
              </div>
            )}
          </div>

          <button onClick={onRefreshConnection} className={`${styles.actionButton} ${styles.primaryButton}`}>
            <RefreshCw size={16} />
            {t('refresh_connection') || 'Refresh Connection'}
          </button>
        </div>

        {/* Audio Status */}
        <div className={styles.section}>
          <div className={styles.sectionHeader}>
            <div className={`${styles.dot} ${
              audioReady ? styles.dotConnected :
              audioBlockedByPolicy ? styles.dotConnecting : styles.dotInactive
            }`} />
            <h4 className={styles.sectionTitle}>{t('notification_sound') || 'Notification Sound'}</h4>
          </div>

          <div className={styles.infoCard}>
            <div className={styles.row}>
              <span className={styles.label}>{t('sound_enabled') || 'Sound Enabled'}</span>
              <span className={styles.value}>{audioEnabled ? 'Yes' : 'No'}</span>
            </div>
            <div className={styles.row}>
              <span className={styles.label}>{t('audio_status') || 'Audio Status'}</span>
              <span className={`${styles.badge} ${getAudioStatusClass()}`}>
                {audioReady ? 'Ready' : audioBlockedByPolicy ? 'Blocked' : 'Disabled'}
              </span>
            </div>
          </div>

          <div className={styles.buttonGroup}>
            {audioBlockedByPolicy && (
              <button onClick={onEnableAudio} className={`${styles.actionButton} ${styles.warningButton}`}>
                <Unlock size={16} />
                {t('enable_sound') || 'Enable Sound'}
              </button>
            )}
            <button 
              onClick={onTestSound} 
              disabled={!audioReady} 
              className={`${styles.actionButton} ${styles.secondaryButton}`}
            >
              <Volume2 size={16} />
              {t('test_sound') || 'Test Sound'}
            </button>
          </div>
        </div>

        {/* Tips */}
        {(audioBlockedByPolicy || sseConnectionState === 'error') && (
          <div className={styles.tipsBox}>
            <Info size={16} style={{ color: '#2196f3', flexShrink: 0 }} />
            <div>
              <h5 className={styles.tipsTitle}>{t('tips') || 'Tips'}</h5>
              <ul className={styles.tipsList}>
                {audioBlockedByPolicy && (
                  <>
                    <li>{t('click_or_interact_with_the_page_to_allow_audio') || 'Click or interact with the page to allow audio.'}</li>
                    <li>{t('check_your_browsers_autoplay_settings') || 'Check your browser\'s autoplay settings.'}</li>
                  </>
                )}
                {sseConnectionState === 'error' && (
                  <>
                    <li>{t('check_your_internet_connection') || 'Check your internet connection.'}</li>
                    <li>{t('try_a_hard_refresh') || 'Try a hard refresh (Ctrl+F5).'}.</li>
                  </>
                )}
              </ul>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
