'use client';

import React, { useState, useEffect, useCallback } from 'react';
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
  AlertCircle,
  Server,
  Users,
  Clock,
  CheckCircle,
  XCircle,
  Loader
} from 'lucide-react';
import { getEventsDiagnostics } from '@/services/cashierService';
import { SseDiagnostics, LogEntry, RecentError } from '@/types/diagnostics';
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

  // Server diagnostics state
  const [serverDiagnostics, setServerDiagnostics] = useState<SseDiagnostics | null>(null);
  const [serverLoading, setServerLoading] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(false);

  const fetchServerDiagnostics = useCallback(async () => {
    setServerLoading(true);
    setServerError(null);
    try {
      const data = await getEventsDiagnostics();
      setServerDiagnostics(data);
    } catch (err) {
      setServerError(err instanceof Error ? err.message : 'Failed to fetch diagnostics');
    } finally {
      setServerLoading(false);
    }
  }, []);

  // Fetch on mount
  useEffect(() => {
    fetchServerDiagnostics();
  }, [fetchServerDiagnostics]);

  // Auto-refresh every 5 seconds if enabled
  useEffect(() => {
    if (!autoRefresh) return;
    const interval = setInterval(fetchServerDiagnostics, 5000);
    return () => clearInterval(interval);
  }, [autoRefresh, fetchServerDiagnostics]);

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

  const formatDuration = (duration: string) => {
    // Duration comes as "HH:MM:SS.sssssss" format
    if (!duration) return 'Unknown';
    const match = duration.match(/^(\d+):(\d+):(\d+)/);
    if (match) {
      const hours = parseInt(match[1]);
      const minutes = parseInt(match[2]);
      const seconds = parseInt(match[3]);
      if (hours > 0) return `${hours}h ${minutes}m`;
      if (minutes > 0) return `${minutes}m ${seconds}s`;
      return `${seconds}s`;
    }
    return duration;
  };

  const formatTimestamp = (timestamp: string) => {
    try {
      const date = new Date(timestamp);
      return date.toLocaleTimeString();
    } catch {
      return timestamp;
    }
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

        {/* Server Diagnostics Section */}
        <div className={styles.section}>
          <div className={styles.sectionHeader}>
            <Server size={16} style={{ color: '#2196f3' }} />
            <h4 className={styles.sectionTitle}>{t('server_diagnostics') || 'Server Diagnostics'}</h4>
            <button
              onClick={fetchServerDiagnostics}
              className={styles.iconButton}
              disabled={serverLoading}
              title="Refresh server diagnostics"
            >
              <RefreshCw size={14} className={serverLoading ? styles.spinning : ''} />
            </button>
          </div>

          {serverLoading && !serverDiagnostics && (
            <div className={styles.infoCard}>
              <div className={styles.loadingRow}>
                <Loader size={16} className={styles.spinning} />
                <span>Loading server diagnostics...</span>
              </div>
            </div>
          )}

          {serverError && (
            <div className={styles.errorBox}>
              <AlertCircle size={14} className={styles.errorIcon} />
              <p className={styles.errorText}>{serverError}</p>
            </div>
          )}

          {serverDiagnostics && (
            <>
              {/* Connected Clients Summary */}
              <div className={styles.infoCard}>
                <div className={styles.row}>
                  <span className={styles.label}>
                    <Users size={14} style={{ marginRight: '6px' }} />
                    Total Clients
                  </span>
                  <span className={`${styles.badge} ${styles.badgeConnected}`}>
                    {serverDiagnostics.totalClients}
                  </span>
                </div>
                <div className={styles.clientBreakdown}>
                  <span className={styles.clientType}>Kitchen: {serverDiagnostics.kitchenClients}</span>
                  <span className={styles.clientType}>Service: {serverDiagnostics.serviceClients}</span>
                  <span className={styles.clientType}>Manager: {serverDiagnostics.managerClients}</span>
                  <span className={styles.clientType}>Stock: {serverDiagnostics.stockClients}</span>
                </div>
                <div className={styles.row}>
                  <span className={styles.label}>
                    <CheckCircle size={14} style={{ marginRight: '6px', color: '#4caf50' }} />
                    Successful Sends
                  </span>
                  <span className={styles.value}>{serverDiagnostics.totalSuccessfulSends}</span>
                </div>
                <div className={styles.row}>
                  <span className={styles.label}>
                    <XCircle size={14} style={{ marginRight: '6px', color: '#f44336' }} />
                    Failed Sends
                  </span>
                  <span className={styles.value} style={{ color: serverDiagnostics.totalFailedSends > 0 ? '#f44336' : 'inherit' }}>
                    {serverDiagnostics.totalFailedSends}
                  </span>
                </div>
                {serverDiagnostics.clientsWithErrors > 0 && (
                  <div className={styles.row}>
                    <span className={styles.label}>
                      <AlertCircle size={14} style={{ marginRight: '6px', color: '#ff9800' }} />
                      Clients with Errors
                    </span>
                    <span className={styles.value} style={{ color: '#ff9800' }}>
                      {serverDiagnostics.clientsWithErrors}
                    </span>
                  </div>
                )}
              </div>

              {/* Recent Logs */}
              {serverDiagnostics.recentLogs && serverDiagnostics.recentLogs.length > 0 && (
                <div className={styles.logsSection}>
                  <h5 className={styles.subSectionTitle}>
                    <Clock size={14} />
                    Recent Server Logs
                  </h5>
                  <div className={styles.logsList}>
                    {serverDiagnostics.recentLogs.slice(0, 10).map((log: LogEntry, index: number) => (
                      <div key={index} className={`${styles.logEntry} ${styles[`log${log.level}`]}`}>
                        <span className={styles.logTime}>{formatTimestamp(log.timestamp)}</span>
                        <span className={styles.logLevel}>[{log.level}]</span>
                        <span className={styles.logMessage}>{log.message}</span>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Recent Errors */}
              {serverDiagnostics.recentErrors && serverDiagnostics.recentErrors.length > 0 && (
                <div className={styles.errorsSection}>
                  <h5 className={styles.subSectionTitle} style={{ color: '#f44336' }}>
                    <AlertCircle size={14} />
                    Recent Errors ({serverDiagnostics.recentErrors.length})
                  </h5>
                  <div className={styles.errorsList}>
                    {serverDiagnostics.recentErrors.slice(0, 5).map((error: RecentError, index: number) => (
                      <div key={index} className={styles.errorEntry}>
                        <div className={styles.errorHeader}>
                          <span className={styles.errorTime}>{formatTimestamp(error.timestamp)}</span>
                          <span className={styles.errorType}>{error.errorType}</span>
                        </div>
                        <div className={styles.errorDetails}>
                          <span>{error.clientType} client from {error.country}</span>
                          <span className={styles.errorMessage}>{error.message}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Auto-refresh toggle */}
              <div className={styles.row} style={{ marginTop: '12px' }}>
                <label className={styles.checkboxLabel}>
                  <input
                    type="checkbox"
                    checked={autoRefresh}
                    onChange={(e) => setAutoRefresh(e.target.checked)}
                  />
                  Auto-refresh every 5s
                </label>
                <span className={styles.timestamp}>
                  Updated: {formatTimestamp(serverDiagnostics.timestamp)}
                </span>
              </div>
            </>
          )}
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
