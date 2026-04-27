import React from 'react';
import { useTranslation } from 'react-i18next';
import styles from './ServerHeader.module.css';

interface ServerHeaderProps {
  isConnected: boolean;
  connectionState: 'connecting' | 'connected' | 'disconnected' | 'error';
  lastEventTime: Date | null;
  error: string | null;
  statusFilter: string;
  onStatusFilterChange: (filter: string) => void;
}

export default function ServerHeader({
  isConnected: _isConnected,
  connectionState,
  lastEventTime,
  error,
  statusFilter,
  onStatusFilterChange,
}: ServerHeaderProps) {
  const { t } = useTranslation();

  const getConnectionStatusClass = () => {
    switch (connectionState) {
      case 'connected':
        return styles.statusConnected;
      case 'connecting':
        return styles.statusConnecting;
      case 'error':
      case 'disconnected':
        return styles.statusDisconnected;
      default:
        return styles.statusDisconnected;
    }
  };

  const getConnectionStatusText = () => {
    switch (connectionState) {
      case 'connected':
        return t('server.connected', 'Connected');
      case 'connecting':
        return t('server.connecting', 'Connecting...');
      case 'error':
        return t('server.connection_error', 'Connection Error');
      case 'disconnected':
        return t('server.disconnected', 'Disconnected');
      default:
        return t('server.unknown', 'Unknown');
    }
  };

  return (
    <header className={styles.header}>
      <div className={styles.leftSection}>
        <h1 className={styles.title}>🍽️ {t('server.title', 'Server Interface')}</h1>
        {error && <span className={styles.errorBadge}>{error}</span>}
      </div>

      <div className={styles.centerSection}>
        <select
          className={styles.filterSelect}
          value={statusFilter}
          onChange={(e) => onStatusFilterChange(e.target.value)}
        >
          <option value="active">{t('server.filter_active', 'Active Orders')}</option>
          <option value="all">{t('server.filter_all', 'All Orders')}</option>
          <option value="Pending">{t('server.filter_pending', 'Pending')}</option>
          <option value="Confirmed">{t('server.filter_confirmed', 'Confirmed')}</option>
          <option value="Preparing">{t('server.filter_preparing', 'Preparing')}</option>
          <option value="Ready">{t('server.filter_ready', 'Ready for Pickup')}</option>
        </select>
      </div>

      <div className={styles.rightSection}>
        <div className={`${styles.connectionStatus} ${getConnectionStatusClass()}`}>
          <span className={styles.statusDot}></span>
          <span className={styles.statusText}>{getConnectionStatusText()}</span>
        </div>
        {lastEventTime && (
          <span className={styles.lastUpdate}>
            {t('server.last_update', 'Last update')}: {lastEventTime.toLocaleTimeString()}
          </span>
        )}
      </div>
    </header>
  );
}
